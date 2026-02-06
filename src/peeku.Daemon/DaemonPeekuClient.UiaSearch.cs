using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  private static AutomationElement? FindByRefId(AutomationElement root, string refId, int maxNodes, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(refId))
    {
      return null;
    }

    if (maxNodes <= 0)
    {
      return null;
    }

    var stack = new Stack<AutomationElement>(capacity: 256);
    stack.Push(root);

    var seen = 0;
    while (stack.Count > 0)
    {
      ct.ThrowIfCancellationRequested();

      var el = stack.Pop();
      seen++;
      if (seen > maxNodes)
      {
        return null;
      }

      try
      {
        if (string.Equals(UiaRefId.Create(el), refId, StringComparison.Ordinal))
        {
          return el;
        }
      }
      catch
      {
      }

      AutomationElement[] children;
      try
      {
        children = el.FindAllChildren();
      }
      catch
      {
        continue;
      }

      for (var i = 0; i < children.Length; i++)
      {
        stack.Push(children[i]);
      }
    }

    return null;
  }
}
