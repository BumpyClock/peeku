using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient
{
  private static string? CombineWarnings(string? a, string? b)
  {
    if (string.IsNullOrWhiteSpace(a))
    {
      return string.IsNullOrWhiteSpace(b) ? null : b;
    }

    if (string.IsNullOrWhiteSpace(b))
    {
      return a;
    }

    return $"{a} {b}";
  }

  private static (AutomationElement? Root, string? Warning) ResolveRootWithWarning(Target target, UIA3Automation automation, CancellationToken ct)
  {
    var root = ResolveRoot(target, automation, ct, out var warning);
    return (root, warning);
  }

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

  private static bool TryClickViaUiaPatterns(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support click via UIA patterns.");

    try
    {
      if (element.Patterns.Invoke.IsSupported)
      {
        element.Patterns.Invoke.Pattern.Invoke();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.Toggle.IsSupported)
      {
        element.Patterns.Toggle.Pattern.Toggle();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA toggle failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    try
    {
      if (element.Patterns.SelectionItem.IsSupported)
      {
        element.Patterns.SelectionItem.Pattern.Select();
        return true;
      }
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA select failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }

    return false;
  }

  private static bool TryInvoke(AutomationElement element, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support invoke pattern.");

    if (!element.Patterns.Invoke.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Invoke.Pattern.Invoke();
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA invoke failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TrySetValue(AutomationElement element, string value, out PeekuError error)
  {
    error = PeekuErrors.Create(PeekuErrorCode.NotSupported, "Element does not support value pattern.");

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      element.Patterns.Value.Pattern.SetValue(value ?? "");
      return true;
    }
    catch (Exception ex)
    {
      error = PeekuErrors.Create(PeekuErrorCode.Internal, "UIA set value failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult });
      return false;
    }
  }

  private static bool TryGetValue(AutomationElement element, out string? value)
  {
    value = null;

    if (!element.Patterns.Value.IsSupported)
    {
      return false;
    }

    try
    {
      value = element.Patterns.Value.Pattern.Value;
      return true;
    }
    catch
    {
      value = null;
      return false;
    }
  }

  /// <summary>
  /// Bounded DFS over the live UIA tree, collecting up to <paramref name="maxCollect"/> elements
  /// (with name or automationId) for candidate-hint ranking. Visits at most
  /// <paramref name="maxVisited"/> nodes to stay cheap on large trees.
  /// </summary>
  private static IReadOnlyList<CandidateHints.Candidate> CollectLiveCandidatesInProc(
    AutomationElement root,
    string? intent,
    int maxVisited,
    int maxCollect,
    CancellationToken ct)
  {
    var pool = new List<UiaElement>(capacity: Math.Min(maxCollect * 4, 64));
    var stack = new Stack<AutomationElement>(capacity: 64);
    stack.Push(root);
    var visited = 0;

    while (stack.Count > 0 && visited < maxVisited)
    {
      ct.ThrowIfCancellationRequested();
      var el = stack.Pop();
      visited++;

      string? name = null;
      string? automationId = null;
      string? controlType = null;
      Rect? rect = null;

      try { name = el.Name; } catch { }
      try { automationId = el.AutomationId; } catch { }
      try { controlType = el.ControlType.ToString(); } catch { }
      rect = ReadRect(el, UiaPropertiesMode.Basic);

      if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(automationId))
      {
        pool.Add(new UiaElement(
          Element: new ElementRef(""),
          Rect: rect,
          Name: string.IsNullOrWhiteSpace(name) ? null : name,
          ControlType: string.IsNullOrWhiteSpace(controlType) ? null : controlType,
          AutomationId: string.IsNullOrWhiteSpace(automationId) ? null : automationId));
      }

      AutomationElement[] children;
      try { children = el.FindAllChildren(); }
      catch { continue; }

      for (var i = 0; i < children.Length; i++)
      {
        stack.Push(children[i]);
      }
    }

    return CandidateHints.Suggest(pool, intent, maxCollect);
  }
}
