using System.Text.Json;
using System.Text.Json.Serialization;
using peeku;

namespace peeku.Daemon;

/// <summary>
/// Daemon entry point for the named-pipe JSON-RPC server.
/// </summary>
/// <example>
/// <code>
/// await Program.Main(new[] { "--pipeName", "peeku.user.v1" });
/// </code>
/// </example>
public sealed class Program
{
  public static async Task<int> Main(string[] args)
  {
    // P1b-S0: set DPI awareness before DaemonSession constructs UIA3Automation (process-wide, must
    // precede the first COM/UIA init on the actor thread).
    Win32Screen.EnsureProcessDpiAware();

    var pipeName = ResolvePipeName(args);
    var idleTimeout = ResolveIdleTimeout(args);
    var markerPath = ResolveMarkerPath(args);
    var shutdown = new DaemonShutdown();
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      shutdown.Request();
      cts.Cancel();
    };

    var options = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };

    var codec = new JsonRpcCodec(options);
    var dispatcher = new JsonRpcDispatcher(options);

    // Singleton DaemonSession for the whole daemon process lifetime, shared across every pipe
    // connection. This is what makes a `uia.snapshot` in one CLI process and a follow-up
    // `set-value/click/element get --ref <refId>` in a SEPARATE CLI process resolve: both hit the
    // SAME warm UIA3Automation + the SAME HandleIdCache instead of a fresh empty cache per command.
    //
    // INVARIANT (do not break): sharing one session — and reusing the AutomationElement COM refs
    // it caches — is safe ONLY because (1) DaemonServer.RunAsync processes one connection at a time
    // (serial while-loop, no fan-out) so connection B cannot start until A returns, AND
    // (2) DaemonSession runs a single, process-lifetime MTA actor thread; every UIA touch marshals
    // through session.ExecuteAsync, so cached COM refs are only ever touched on that one MTA thread
    // and never cross apartments. A future concurrent server, a second actor thread, or switching
    // the actor thread to STA would silently break this. Keep the server serial and the actor MTA,
    // or add explicit synchronization around the cache and COM refs.
    using var session = new DaemonSession();

    var connection = new JsonRpcConnection(codec, dispatcher, session, shutdown);
    var server = new DaemonServer(pipeName, connection, shutdown, idleTimeout, markerPath);

    try
    {
      await server.RunAsync(cts.Token).ConfigureAwait(false);
      return 0;
    }
    catch (OperationCanceledException)
    {
      return 0;
    }
    catch (Exception ex)
    {
      var message = string.IsNullOrWhiteSpace(ex.Message) ? "Daemon failed" : ex.Message;
      Console.Error.WriteLine(message);
      return 1;
    }
  }

  private static string ResolvePipeName(string[] args)
  {
    if (args is null)
    {
      throw new ArgumentNullException(nameof(args));
    }

    var fallback = BuildDefaultPipeName();

    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i] ?? "";
      if (!string.Equals(arg, "--pipeName", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (i + 1 >= args.Length)
      {
        throw new ArgumentException("Pipe name value is required", nameof(args));
      }

      var value = args[i + 1] ?? "";
      if (string.IsNullOrWhiteSpace(value))
      {
        throw new ArgumentException("Pipe name value is required", nameof(args));
      }

      return value.Trim();
    }

    return fallback;
  }

  private static string BuildDefaultPipeName()
  {
    var user = Environment.UserName ?? "";
    var normalized = string.IsNullOrWhiteSpace(user) ? "user" : user.Trim();
    return $"peeku.{normalized}.v1";
  }

  /// <summary>
  /// Parses --idle-timeout &lt;minutes&gt;. Absent, non-numeric, or &lt;= 0 returns TimeSpan.Zero
  /// (persistent — no reap).
  /// </summary>
  private static TimeSpan ResolveIdleTimeout(string[] args)
  {
    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i] ?? "";
      if (!string.Equals(arg, "--idle-timeout", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (i + 1 >= args.Length)
      {
        return TimeSpan.Zero;
      }

      var value = args[i + 1] ?? "";
      if (int.TryParse(value, out var minutes) && minutes > 0)
      {
        return TimeSpan.FromMinutes(minutes);
      }

      return TimeSpan.Zero;
    }

    return TimeSpan.Zero;
  }

  /// <summary>
  /// Parses --marker-path "&lt;absolute path&gt;". Returns null if absent or empty.
  /// </summary>
  private static string? ResolveMarkerPath(string[] args)
  {
    for (var i = 0; i < args.Length; i++)
    {
      var arg = args[i] ?? "";
      if (!string.Equals(arg, "--marker-path", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (i + 1 >= args.Length)
      {
        return null;
      }

      var value = (args[i + 1] ?? "").Trim();
      return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    return null;
  }
}
