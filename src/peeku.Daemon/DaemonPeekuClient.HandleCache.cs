using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private string StoreHandle(AutomationElement element)
  {
    var stableKey = TryGetStableKey(element);
    return _handles.Store(element, stableKey);
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
