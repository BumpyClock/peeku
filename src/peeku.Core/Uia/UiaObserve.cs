using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

internal static class UiaObserve
{
  internal static async IAsyncEnumerable<ObservationEvent> ObserveAsync(
    ObserveRequest req,
    [EnumeratorCancellation] CancellationToken ct)
  {
    if (req is null)
    {
      yield break;
    }

    var duration = req.Duration ?? TimeSpan.FromSeconds(10);
    if (duration < TimeSpan.Zero)
    {
      duration = TimeSpan.Zero;
    }

    var maxEvents = req.MaxEvents;
    if (maxEvents < 0)
    {
      maxEvents = 0;
    }

    var wantFocus = req.Events is ObserveEventSet.Focus or ObserveEventSet.All;
    if (!wantFocus || duration == TimeSpan.Zero || maxEvents == 0)
    {
      yield break;
    }

    var channel = Channel.CreateBounded<AutomationElement>(
      new BoundedChannelOptions(capacity: 256)
      {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
      });

    using var automation = new UIA3Automation();
    using var handler = automation.RegisterFocusChangedEvent(el =>
    {
      if (el is null)
      {
        return;
      }

      _ = channel.Writer.TryWrite(el);
    });

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(duration);

    var token = timeoutCts.Token;
    var count = 0;
    var lastRefId = default(string);

    while (count < maxEvents)
    {
      AutomationElement el;
      try
      {
        el = await channel.Reader.ReadAsync(token).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        yield break;
      }
      catch
      {
        yield break;
      }

      string refId;
      try
      {
        refId = UiaRefId.Create(el);
      }
      catch
      {
        continue;
      }

      if (string.Equals(lastRefId, refId, StringComparison.Ordinal))
      {
        continue;
      }

      lastRefId = refId;
      count++;

      var data = new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        ["elementRef"] = new ElementRef(refId),
        ["name"] = Safe(() => el.Name),
        ["controlType"] = Safe(() => el.ControlType.ToString()),
        ["processId"] = Safe(() => el.Properties.ProcessId.ValueOrDefault),
      };

      yield return new ObservationEvent(
        Timestamp: DateTimeOffset.UtcNow,
        EventType: "focusChanged",
        Data: data);
    }
  }

  private static object? Safe(Func<object?> f)
  {
    try
    {
      var v = f();
      return v is string s && string.IsNullOrWhiteSpace(s) ? null : v;
    }
    catch
    {
      return null;
    }
  }
}

