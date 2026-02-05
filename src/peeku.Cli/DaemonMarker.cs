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
