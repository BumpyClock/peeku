using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace peeku;

public sealed partial class UiaClient : IPeekuClient
{
  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return Task.FromResult(new UiaSnapshotResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotId: "",
          Root: new UiaNode(new ElementRef("")),
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required.")));
      }

      if (req.Depth < 0)
      {
        return Task.FromResult(new UiaSnapshotResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotId: "",
          Root: new UiaNode(new ElementRef("")),
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Depth must be >= 0.")));
      }

      if (req.MaxNodes <= 0)
      {
        return Task.FromResult(new UiaSnapshotResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotId: "",
          Root: new UiaNode(new ElementRef("")),
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "MaxNodes must be >= 1.")));
      }

      if (!Enum.IsDefined(typeof(UiaPropertiesMode), req.IncludeProperties))
      {
        return Task.FromResult(new UiaSnapshotResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotId: "",
          Root: new UiaNode(new ElementRef("")),
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "IncludeProperties is invalid.")));
      }

      using var automation = new UIA3Automation();
      var snapshotId = Guid.NewGuid().ToString("N");

      var warning = default(string);
      var rootElement = ResolveRoot(req.Target, automation, ct, out warning);
      if (rootElement is null)
      {
        return Task.FromResult(new UiaSnapshotResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotId: "",
          Root: new UiaNode(new ElementRef("")),
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found.")));
      }

      var nodesCaptured = 0;
      var truncated = false;
      var elements = new List<UiaElement>(capacity: Math.Clamp(req.MaxNodes, 1, 8192));

      var rootRefId = UiaRefId.Create(rootElement);
      var rootBuilder = new NodeBuilder(
        RefId: rootRefId,
        Name: ReadName(rootElement, req.IncludeProperties),
        ControlType: ReadControlType(rootElement, req.IncludeProperties),
        Element: rootElement);

      nodesCaptured++;
      elements.Add(ReadElement(rootBuilder.RefId, snapshotId, rootElement, req.IncludeProperties));

      var stack = new Stack<(NodeBuilder Node, int RemainingDepth)>();
      stack.Push((rootBuilder, req.Depth));

      while (stack.Count > 0)
      {
        ct.ThrowIfCancellationRequested();

        var (node, remainingDepth) = stack.Pop();
        if (remainingDepth <= 0)
        {
          continue;
        }

        AutomationElement[] children;
        try
        {
          children = node.Element.FindAllChildren();
        }
        catch
        {
          continue;
        }

        if (children.Length <= 0)
        {
          continue;
        }

        node.Children ??= new List<NodeBuilder>(capacity: Math.Clamp(children.Length, 0, 256));

        foreach (var child in children)
        {
          ct.ThrowIfCancellationRequested();

          if (nodesCaptured >= req.MaxNodes)
          {
            truncated = true;
            stack.Clear();
            break;
          }

          var childRefId = UiaRefId.Create(child);
          var childBuilder = new NodeBuilder(
            RefId: childRefId,
            Name: ReadName(child, req.IncludeProperties),
            ControlType: ReadControlType(child, req.IncludeProperties),
            Element: child);

          node.Children.Add(childBuilder);
          nodesCaptured++;
          elements.Add(ReadElement(childBuilder.RefId, snapshotId, child, req.IncludeProperties));

          stack.Push((childBuilder, remainingDepth - 1));
        }
      }

      var root = BuildNode(rootBuilder, snapshotId);

      if (truncated)
      {
        warning = string.IsNullOrWhiteSpace(warning)
          ? $"Snapshot truncated at MaxNodes={req.MaxNodes}."
          : $"{warning} Snapshot truncated at MaxNodes={req.MaxNodes}.";
      }

      return Task.FromResult(new UiaSnapshotResult(
        Ok: true,
        Meta: scope.Meta(warning: warning),
        SnapshotId: snapshotId,
        Root: root,
        Elements: elements));
    }
    catch (OperationCanceledException)
    {
      return Task.FromResult(new UiaSnapshotResult(
        Ok: false,
        Meta: scope.Meta(),
        SnapshotId: "",
        Root: new UiaNode(new ElementRef("")),
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")));
    }
    catch (Exception ex)
    {
      return Task.FromResult(new UiaSnapshotResult(
        Ok: false,
        Meta: scope.Meta(),
        SnapshotId: "",
        Root: new UiaNode(new ElementRef("")),
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "UIA snapshot failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult })));
    }
  }

  private static AutomationElement? ResolveRoot(Target target, UIA3Automation automation, CancellationToken ct, out string? warning)
  {
    warning = null;

    switch (target)
    {
      case Target.Desktop:
        return automation.GetDesktop();

      case Target.FocusedWindow:
      {
        var win = Win32Windows.GetFocusedWindow();
        if (win is null)
        {
          warning = "No foreground window.";
          return null;
        }

        if (!TryParseHwndHex(win.HwndHex, out var hwnd))
        {
          warning = "Foreground window handle invalid.";
          return null;
        }

        return automation.FromHandle(hwnd);
      }

      case Target.WindowByHwnd hwndTarget:
      {
        if (!TryParseHwndHex(hwndTarget.HwndHex, out var hwnd))
        {
          warning = "Window handle invalid.";
          return null;
        }

        return automation.FromHandle(hwnd);
      }

      case Target.WindowByQuery queryTarget:
      {
        ct.ThrowIfCancellationRequested();

        var q = queryTarget.Query;
        var candidates = Win32Windows.ListWindows(
          new WindowsListRequest(TitleContains: q.TitleContains, ProcessName: q.ProcessName, Limit: 200),
          ct);

        var match = candidates.FirstOrDefault(w => q.ProcessId is null || w.ProcessId == q.ProcessId.Value);
        if (match is null)
        {
          warning = "No window matched query.";
          return null;
        }

        if (!TryParseHwndHex(match.HwndHex, out var hwnd))
        {
          warning = "Matched window handle invalid.";
          return null;
        }

        return automation.FromHandle(hwnd);
      }

      case Target.Screen:
        warning = "Screen target not supported for UIA snapshot.";
        return null;

      default:
        warning = "Target not supported.";
        return null;
    }
  }

  private static bool TryParseHwndHex(string hwndHex, out nint hwnd)
  {
    hwnd = 0;
    if (string.IsNullOrWhiteSpace(hwndHex))
    {
      return false;
    }

    var s = hwndHex.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      s = s[2..];
    }

    if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
    {
      return false;
    }

    hwnd = unchecked((nint)value);
    return hwnd != 0;
  }

  private static string? ReadName(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var name = element.Name;
      if (!string.IsNullOrWhiteSpace(name))
      {
        return name;
      }
    }
    catch
    {
    }

    return UiaNameFallbacks.ReadName(element);
  }

  private static string? ReadControlType(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      return element.ControlType.ToString();
    }
    catch
    {
      return null;
    }
  }

  private static Rect? ReadRect(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.Basic && mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var r = element.BoundingRectangle;
      if (r.Width <= 0 || r.Height <= 0)
      {
        return null;
      }

      return new Rect(
        X: r.Left,
        Y: r.Top,
        Width: r.Width,
        Height: r.Height);
    }
    catch
    {
      return null;
    }
  }

  private static string? ReadAutomationId(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var id = element.AutomationId;
      return string.IsNullOrWhiteSpace(id) ? null : id;
    }
    catch
    {
      return null;
    }
  }

  private static string? ReadClassName(AutomationElement element, UiaPropertiesMode mode)
  {
    if (mode != UiaPropertiesMode.All)
    {
      return null;
    }

    try
    {
      var cn = element.ClassName;
      return string.IsNullOrWhiteSpace(cn) ? null : cn;
    }
    catch
    {
      return null;
    }
  }

  private static UiaElement ReadElement(string refId, string snapshotId, AutomationElement element, UiaPropertiesMode mode)
    => new(
      Element: new ElementRef(refId, snapshotId),
      Rect: ReadRect(element, mode),
      Name: ReadName(element, mode),
      ControlType: ReadControlType(element, mode),
      AutomationId: ReadAutomationId(element, mode),
      ClassName: ReadClassName(element, mode));

  private static UiaNode BuildNode(NodeBuilder node, string snapshotId)
    => new(
      Element: new ElementRef(node.RefId, snapshotId),
      Name: node.Name,
      ControlType: node.ControlType,
      Children: node.Children is null ? null : node.Children.Select(c => BuildNode(c, snapshotId)).ToArray());

  private sealed record NodeBuilder(
    string RefId,
    string? Name,
    string? ControlType,
    AutomationElement Element)
  {
    public List<NodeBuilder>? Children { get; set; }
  }

  public Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();

  public Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
    => UiaWait.WaitAsync(req, UiaSnapshotAsync, ct);

  public Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => throw new NotImplementedException();
}
