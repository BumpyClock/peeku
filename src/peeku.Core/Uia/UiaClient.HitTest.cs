using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient
{
  public Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return Task.FromResult(new ElementAtPointResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Ancestors: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required.")));
      }

      using var automation = new UIA3Automation();

      AutomationElement? hit;
      try
      {
        hit = automation.FromPoint(new System.Drawing.Point(req.X, req.Y));
      }
      catch (Exception ex)
      {
        return Task.FromResult(new ElementAtPointResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Ancestors: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "FromPoint failed.",
            new { x = req.X, y = req.Y, exception = ex.GetType().FullName, ex.Message, ex.HResult })));
      }

      if (hit is null)
      {
        return Task.FromResult(new ElementAtPointResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Ancestors: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(
            PeekuErrorCode.ElementNotFound,
            "No element found at point.",
            new { x = req.X, y = req.Y })));
      }

      string hitRefId;
      try
      {
        hitRefId = UiaRefId.Create(hit);
      }
      catch (Exception ex)
      {
        return Task.FromResult(new ElementAtPointResult(
          Ok: false,
          Meta: scope.Meta(),
          Element: new UiaElement(new ElementRef("")),
          Ancestors: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(
            PeekuErrorCode.Internal,
            "Failed to compute element refId.",
            new { x = req.X, y = req.Y, exception = ex.GetType().FullName, ex.Message, ex.HResult })));
      }

      var hitElement = ReadElement(hitRefId, "", hit, req.IncludeProperties);

      // Walk ancestor chain: .Parent until null or desktop (no children → desktop check via ControlType).
      var ancestorElements = new List<AutomationElement>(capacity: 12);
      const int MaxAncestors = 40;
      var cursor = hit;
      for (var i = 0; i < MaxAncestors; i++)
      {
        AutomationElement? parent;
        try
        {
          parent = cursor.Parent;
        }
        catch
        {
          break;
        }

        if (parent is null)
        {
          break;
        }

        // Stop at desktop root (ControlType = Pane with no parent is the UIA desktop).
        try
        {
          var parentType = parent.ControlType.ToString();
          if (string.Equals(parentType, "Desktop", StringComparison.OrdinalIgnoreCase))
          {
            ancestorElements.Add(parent);
            break;
          }
        }
        catch
        {
        }

        ancestorElements.Add(parent);
        cursor = parent;
      }

      // Reverse to root-first order.
      ancestorElements.Reverse();

      var ancestors = new List<UiaElement>(capacity: ancestorElements.Count);
      foreach (var ancestor in ancestorElements)
      {
        string ancestorRefId;
        try
        {
          ancestorRefId = UiaRefId.Create(ancestor);
        }
        catch
        {
          continue;
        }

        ancestors.Add(ReadElement(ancestorRefId, "", ancestor, req.IncludeProperties));
      }

      return Task.FromResult(new ElementAtPointResult(
        Ok: true,
        Meta: scope.Meta(),
        Element: hitElement,
        Ancestors: ancestors));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new ElementAtPointResult(
        Ok: false,
        Meta: scope.Meta(),
        Element: new UiaElement(new ElementRef("")),
        Ancestors: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new ElementAtPointResult(
        Ok: false,
        Meta: scope.Meta(),
        Element: new UiaElement(new ElementRef("")),
        Ancestors: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Element at-point failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }
}
