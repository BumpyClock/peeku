using System.Diagnostics;

namespace peeku;

public static class Results
{
  public static string TraceId(string? traceId = null)
  {
    if (!string.IsNullOrWhiteSpace(traceId))
    {
      return traceId.Trim();
    }

    var current = Activity.Current;
    if (current is not null)
    {
      return current.TraceId.ToString();
    }

    return ActivityTraceId.CreateRandom().ToString();
  }

  public static ResultScope Start(string? traceId = null, DateTimeOffset? timestamp = null)
    => new(TraceId(traceId), timestamp ?? DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());

  public static ResultMeta Meta(
    string? traceId = null,
    DateTimeOffset? timestamp = null,
    int durationMs = 0,
    string? warning = null)
    => new(TraceId(traceId), timestamp ?? DateTimeOffset.UtcNow, durationMs < 0 ? 0 : durationMs, warning);
}

public readonly struct ResultScope
{
  private readonly long _startedAt;

  public string TraceId { get; }
  public DateTimeOffset Timestamp { get; }

  internal ResultScope(string traceId, DateTimeOffset timestamp, long startedAt)
  {
    TraceId = traceId;
    Timestamp = timestamp;
    _startedAt = startedAt;
  }

  public int DurationMs
  {
    get
    {
      var ms = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds;
      if (ms <= 0)
      {
        return 0;
      }

      if (ms >= int.MaxValue)
      {
        return int.MaxValue;
      }

      return (int)ms;
    }
  }

  public ResultMeta Meta(string? warning = null)
  {
    var traceId = Results.TraceId(TraceId);
    var timestamp = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;
    return new(traceId, timestamp, DurationMs, warning);
  }

  public ResultMeta Meta(int durationMs, string? warning = null)
  {
    var traceId = Results.TraceId(TraceId);
    var timestamp = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;
    return new(traceId, timestamp, durationMs < 0 ? 0 : durationMs, warning);
  }
}

public enum PeekuErrorCode
{
  Unknown = 0,
  InvalidArgument = 1,
  Timeout = 2,
  Unavailable = 3,
  PermissionDenied = 4,
  NotSupported = 5,
  Canceled = 6,
  Internal = 7,
  NotFound = 8,

  ElementNotFound = 100,
  WindowNotFound = 101,
  SnapshotNotFound = 102,
}

public static class PeekuErrors
{
  public static PeekuError Create(PeekuErrorCode code, string message, object? details = null)
    => new(Code(code), message, details);

  public static string Code(PeekuErrorCode code) => code switch
  {
    PeekuErrorCode.Unknown => "Unknown",
    PeekuErrorCode.InvalidArgument => "InvalidArgument",
    PeekuErrorCode.Timeout => "Timeout",
    PeekuErrorCode.Unavailable => "Unavailable",
    PeekuErrorCode.PermissionDenied => "PermissionDenied",
    PeekuErrorCode.NotSupported => "NotSupported",
    PeekuErrorCode.Canceled => "Canceled",
    PeekuErrorCode.Internal => "Internal",
    PeekuErrorCode.NotFound => "NotFound",
    PeekuErrorCode.ElementNotFound => "ElementNotFound",
    PeekuErrorCode.WindowNotFound => "WindowNotFound",
    PeekuErrorCode.SnapshotNotFound => "SnapshotNotFound",
    _ => "Unknown",
  };
}
