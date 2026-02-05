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
    var pipeName = ResolvePipeName(args);
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
    var connection = new JsonRpcConnection(codec, dispatcher, shutdown);
    var server = new DaemonServer(pipeName, connection, shutdown);

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
}
