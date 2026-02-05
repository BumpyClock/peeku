using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace peeku;

internal static class UiaNameFallbacks
{
  public static string? ReadName(AutomationElement element)
  {
    try
    {
      var ct = element.ControlType;
      if (ct == ControlType.Tab)
      {
        return ReadTabStripName(element);
      }

      if (ct == ControlType.TabItem)
      {
        return ReadTabItemName(element);
      }
    }
    catch
    {
      return null;
    }

    return null;
  }

  private static string? ReadTabStripName(AutomationElement tabElement)
  {
    var tabItems = FindDescendantsByControlType(tabElement, ControlType.TabItem, maxDepth: 6, maxNodes: 400);
    if (tabItems.Length <= 0)
    {
      return null;
    }

    var names = new List<string>(capacity: Math.Clamp(tabItems.Length, 0, 64));
    foreach (var tabItem in tabItems)
    {
      var name = ReadNonEmpty(() => tabItem.Name) ?? ReadTabItemName(tabItem);
      if (string.IsNullOrWhiteSpace(name))
      {
        continue;
      }

      var isSelected = false;
      try
      {
        if (tabItem.Patterns.SelectionItem.IsSupported)
        {
          isSelected = tabItem.Patterns.SelectionItem.Pattern.IsSelected;
        }
      }
      catch
      {
        isSelected = false;
      }

      names.Add(isSelected ? $"*{name}" : name);
    }

    if (names.Count == 0)
    {
      return null;
    }

    return $"tabs: {string.Join(", ", names)}";
  }

  private static string? ReadTabItemName(AutomationElement tabItem)
  {
    var text = FindFirstDescendantByControlType(tabItem, ControlType.Text, maxDepth: 4, maxNodes: 200);
    return text is null ? null : ReadNonEmpty(() => text.Name);
  }

  private static AutomationElement? FindFirstDescendantByControlType(
    AutomationElement root,
    ControlType controlType,
    int maxDepth,
    int maxNodes)
  {
    var stack = new Stack<(AutomationElement Element, int Depth)>();
    stack.Push((root, 0));

    var nodes = 0;
    while (stack.Count > 0)
    {
      var (current, depth) = stack.Pop();
      if (depth >= maxDepth)
      {
        continue;
      }

      AutomationElement[] children;
      try
      {
        children = current.FindAllChildren();
      }
      catch
      {
        continue;
      }

      foreach (var child in children)
      {
        nodes++;
        if (nodes > maxNodes)
        {
          return null;
        }

        try
        {
          if (child.ControlType == controlType)
          {
            return child;
          }
        }
        catch
        {
          // ignore
        }

        stack.Push((child, depth + 1));
      }
    }

    return null;
  }

  private static AutomationElement[] FindDescendantsByControlType(
    AutomationElement root,
    ControlType controlType,
    int maxDepth,
    int maxNodes)
  {
    var found = new List<AutomationElement>(capacity: 8);
    var stack = new Stack<(AutomationElement Element, int Depth)>();
    stack.Push((root, 0));

    var nodes = 0;
    while (stack.Count > 0)
    {
      var (current, depth) = stack.Pop();
      if (depth >= maxDepth)
      {
        continue;
      }

      AutomationElement[] children;
      try
      {
        children = current.FindAllChildren();
      }
      catch
      {
        continue;
      }

      foreach (var child in children)
      {
        nodes++;
        if (nodes > maxNodes)
        {
          return found.ToArray();
        }

        try
        {
          if (child.ControlType == controlType)
          {
            found.Add(child);
            continue;
          }
        }
        catch
        {
          // ignore
        }

        stack.Push((child, depth + 1));
      }
    }

    return found.ToArray();
  }

  private static string? ReadNonEmpty(Func<string?> f)
  {
    try
    {
      var v = f();
      return string.IsNullOrWhiteSpace(v) ? null : v;
    }
    catch
    {
      return null;
    }
  }
}

