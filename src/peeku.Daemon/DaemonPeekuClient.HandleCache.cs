using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private string StoreHandle(AutomationElement element)
  {
    var stableKey = TryGetStableKey(element);

    // Always mint + index the internal h: handle (fast same-process lookup, stableKey dedup).
    var handle = _handles.Store(element, stableKey);

    // Ship the DURABLE id (uia:pid:hash) on the wire when we have one. It survives cache
    // eviction/TTL/daemon-restart because ResolveActionElement/ElementGetAsync can re-walk the live
    // tree (FindByRefId) to recompute it, where an h: id only ever short-circuits to ElementNotFound.
    // Fall back to the ephemeral h: id only when the element yields no stable key (no RuntimeId and
    // no usable properties) — such refs are non-durable and resolve only via the live cache.
    return string.IsNullOrWhiteSpace(stableKey) ? handle : stableKey;
  }

  private static string? TryGetStableKey(AutomationElement element)
  {
    try
    {
      return UiaRefId.Create(element);
    }
    catch
    {
      return null;
    }
  }
}
