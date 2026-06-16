using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
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

    var wantFocus = (req.Events & ObserveEventSet.Focus) != 0;
    var wantStructure = (req.Events & ObserveEventSet.Structure) != 0;
    var wantProperty = (req.Events & ObserveEventSet.Property) != 0;
    // focus + structure + property are wired (S0/S1/S2): each is a producer writing into
    // the same tagged channel. Bail when nothing is requested.
    if (req.Events == ObserveEventSet.None || duration == TimeSpan.Zero || maxEvents == 0)
    {
      yield break;
    }

    if (!wantFocus && !wantStructure && !wantProperty)
    {
      yield break;
    }

    // Tagged channel: every producer writes a (eventType, element, …) tuple so multiple
    // event sources can multiplex into one stream. The reader switches on EventType.
    var channel = Channel.CreateBounded<RawObservation>(
      new BoundedChannelOptions(capacity: 256)
      {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
      });

    using var automation = new UIA3Automation();

    // Focus is system-wide (no scope arg). Structure is scoped to the desktop subtree —
    // observe is global today (it ignores req.Target); scoping to a target is future work.
    using IDisposable? focusHandler = wantFocus
      ? automation.RegisterFocusChangedEvent(el =>
        {
          if (el is not null)
          {
            _ = channel.Writer.TryWrite(new RawObservation("focusChanged", el, null));
          }
        })
      : null;

    var desktop = (wantStructure || wantProperty) ? automation.GetDesktop() : null;
    using IDisposable? structureHandler = wantStructure
      ? desktop!.RegisterStructureChangedEvent(TreeScope.Subtree, (sender, changeType, runtimeId) =>
        {
          _ = runtimeId;
          if (sender is not null)
          {
            channel.Writer.TryWrite(new RawObservation("structureChanged", sender, changeType));
          }
        })
      : null;

    // Curated property set — a small, state-relevant slice. On the desktop subtree this is
    // high-volume; the bounded channel + maxEvents cap it. (Scoping to req.Target is future work.)
    using IDisposable? propertyHandler = wantProperty
      ? desktop!.RegisterPropertyChangedEvent(
          TreeScope.Subtree,
          (sender, property, newValue) =>
          {
            if (sender is not null)
            {
              channel.Writer.TryWrite(new RawObservation("propertyChanged", sender, Property: property, NewValue: newValue));
            }
          },
          automation.PropertyLibrary.Element.Name,
          automation.PropertyLibrary.Element.IsEnabled,
          automation.PropertyLibrary.Value.Value,
          automation.PropertyLibrary.Toggle.ToggleState)
      : null;

    // WinEvent foreground backstop, folded under Focus: catches window activation that a UIA
    // focus event can miss (no focusable child) and often arrives sooner. Sparse HWND/PID
    // payload tagged source=winevent; runs on its own STA message-pump thread.
    using var foregroundHook = wantFocus
      ? new WinEventHook((hwnd, pid) =>
          channel.Writer.TryWrite(new RawObservation("foregroundChanged", null, Hwnd: hwnd, ForegroundPid: pid)))
      : null;

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(duration);

    var token = timeoutCts.Token;
    var count = 0;
    var lastFocusRefId = default(string);
    var lastForegroundHwnd = default(nint);

    while (count < maxEvents)
    {
      RawObservation raw;
      try
      {
        raw = await channel.Reader.ReadAsync(token).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        yield break;
      }
      catch
      {
        yield break;
      }

      if (raw.EventType == "foregroundChanged")
      {
        // WinEvent backstop: sparse HWND/PID, no UIA enrichment (it came off the pump thread).
        // Collapse consecutive foreground events on the same window.
        if (raw.Hwnd == lastForegroundHwnd)
        {
          continue;
        }

        lastForegroundHwnd = raw.Hwnd;
        count++;

        yield return new ObservationEvent(
          Timestamp: DateTimeOffset.UtcNow,
          EventType: "foregroundChanged",
          Data: new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["hwnd"] = "0x" + raw.Hwnd.ToString("X"),
            ["processId"] = raw.ForegroundPid,
            ["source"] = "winevent",
          });

        continue;
      }

      // Remaining channels are UIA element events: compute the refId once.
      if (raw.Element is not { } el)
      {
        continue;
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

      if (raw.EventType == "focusChanged")
      {
        // Collapse consecutive focus events on the same element.
        if (string.Equals(lastFocusRefId, refId, StringComparison.Ordinal))
        {
          continue;
        }

        lastFocusRefId = refId;
        count++;

        yield return new ObservationEvent(
          Timestamp: DateTimeOffset.UtcNow,
          EventType: "focusChanged",
          Data: new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["elementRef"] = new ElementRef(refId),
            ["name"] = Safe(() => el.Name),
            ["controlType"] = Safe(() => el.ControlType.ToString()),
            ["processId"] = Safe(() => el.Properties.ProcessId.ValueOrDefault),
          });
      }
      else if (raw.EventType == "structureChanged")
      {
        // No dedup: each structure change is a distinct event (the maxEvents cap + bounded
        // channel bound the volume). refId identifies the container whose subtree changed.
        count++;

        yield return new ObservationEvent(
          Timestamp: DateTimeOffset.UtcNow,
          EventType: "structureChanged",
          Data: new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["elementRef"] = new ElementRef(refId),
            ["name"] = Safe(() => el.Name),
            ["controlType"] = Safe(() => el.ControlType.ToString()),
            ["processId"] = Safe(() => el.Properties.ProcessId.ValueOrDefault),
            ["changeType"] = raw.StructureChange?.ToString(),
          });
      }
      else if (raw.EventType == "propertyChanged")
      {
        // No dedup: each property change is a distinct event. UIA reports only the NEW value
        // (no old value), so oldValue stays absent.
        count++;

        yield return new ObservationEvent(
          Timestamp: DateTimeOffset.UtcNow,
          EventType: "propertyChanged",
          Data: new Dictionary<string, object?>(StringComparer.Ordinal)
          {
            ["elementRef"] = new ElementRef(refId),
            ["name"] = Safe(() => el.Name),
            ["controlType"] = Safe(() => el.ControlType.ToString()),
            ["processId"] = Safe(() => el.Properties.ProcessId.ValueOrDefault),
            ["changedProperty"] = raw.Property?.Name,
            ["newValue"] = raw.NewValue?.ToString(),
          });
      }
    }
  }

  /// <summary>
  /// A raw event tagged with its channel, awaiting enrichment on the reader. UIA events carry an
  /// <see cref="Element"/>; the WinEvent foreground backstop carries <see cref="Hwnd"/>/<see cref="ForegroundPid"/> instead.
  /// </summary>
  private readonly record struct RawObservation(
    string EventType,
    AutomationElement? Element,
    StructureChangeType? StructureChange = null,
    PropertyId? Property = null,
    object? NewValue = null,
    nint Hwnd = 0,
    int ForegroundPid = 0);

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

