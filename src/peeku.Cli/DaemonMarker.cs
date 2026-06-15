using System.Diagnostics;
using System.Text.Json;

namespace peeku.Cli;

/// <summary>
/// Represents the daemon marker persisted under LocalAppData and used to reconnect to a running daemon.
/// Example: <code>if (DaemonMarker.TryLoad(out var marker)) { marker.Save(); }</code>
/// </summary>
internal sealed record DaemonMarker(
  string PipeName,
  int Pid,
  DateTimeOffset StartedAt,
  string ProtocolVersion,
  string BuildVersion)
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
  };

  internal static string DefaultPath => Path.Combine(DefaultDirectory, "daemon.json");

  private static string DefaultDirectory
  {
    get
    {
      var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      if (string.IsNullOrWhiteSpace(root))
      {
        throw new InvalidOperationException("Local application data directory is required");
      }

      return Path.Combine(root, "peeku");
    }
  }

  internal static bool TryLoad(out DaemonMarker marker)
    => TryLoad(DefaultPath, out marker);

  internal static bool TryLoad(string path, out DaemonMarker marker)
  {
    marker = new DaemonMarker("unknown", 0, DateTimeOffset.UnixEpoch, "0", "unknown");
    if (string.IsNullOrWhiteSpace(path))
    {
      return false;
    }

    if (!File.Exists(path))
    {
      return false;
    }

    try
    {
      var json = File.ReadAllText(path);
      var loaded = JsonSerializer.Deserialize<DaemonMarker>(json, JsonOptions);
      if (loaded is null)
      {
        return false;
      }

      marker = loaded;
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>Daemon process name (no extension), e.g. <c>peeku-daemon.exe</c> reports <c>peeku-daemon</c>.</summary>
  private const string DaemonProcessName = "peeku-daemon";

  /// <summary>
  /// True when the marker's <see cref="Pid"/> refers to a live process whose name matches the
  /// daemon. A microsecond PID check that lets callers skip a costly pipe round-trip against a
  /// stale marker (a crashed daemon's PID is dead or recycled to an unrelated process).
  /// </summary>
  internal bool IsAlive()
  {
    if (Pid <= 0)
    {
      return false;
    }

    try
    {
      using var proc = Process.GetProcessById(Pid);
      if (proc.HasExited)
      {
        return false;
      }

      // Guard against PID reuse: the recycled PID must still be the daemon.
      return string.Equals(proc.ProcessName, DaemonProcessName, StringComparison.OrdinalIgnoreCase);
    }
    catch (ArgumentException)
    {
      // No process with this id => dead.
      return false;
    }
    catch (InvalidOperationException)
    {
      // Process already exited between lookup and inspection.
      return false;
    }
  }

  /// <summary>Best-effort delete of the marker file at <see cref="DefaultPath"/>; swallows IO races.</summary>
  internal static void TryDeleteStale()
  {
    try
    {
      var path = DefaultPath;
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  internal void Save()
    => Save(DefaultPath);

  internal void Save(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Marker path is required", nameof(path));
    }

    var dir = Path.GetDirectoryName(path);
    if (string.IsNullOrWhiteSpace(dir))
    {
      throw new InvalidOperationException("Marker path directory is required");
    }

    Directory.CreateDirectory(dir);
    var json = JsonSerializer.Serialize(this, JsonOptions);
    File.WriteAllText(path, json);
  }
}
