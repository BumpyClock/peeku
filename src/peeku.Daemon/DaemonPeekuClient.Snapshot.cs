using FlaUI.Core.AutomationElements;
using peeku;

namespace peeku.Daemon;

public sealed partial class DaemonPeekuClient
{
  public Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ThrowIfDisposed();
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

      var snapshotId = Guid.NewGuid().ToString("N");
      var warning = default(string);

      var rootElement = ResolveRoot(req.Target, ct, out warning);
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

      var rootRefId = StoreHandle(rootElement);
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

          var childRefId = StoreHandle(child);
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

  public async Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();

    try
    {
      ThrowIfDisposed();
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(),
          Image: new CaptureImageResult(
            Ok: false,
            Meta: scope.Meta(),
            ImagePath: "",
            MimeType: "image/png",
            Width: 0,
            Height: 0,
            Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required.")),
          SnapshotId: "",
          Elements: Array.Empty<UiaElement>(),
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      var captureReq = new CaptureImageRequest(req.Target, OutPath: null, IncludeBase64: req.IncludeBase64);
      var image = await CaptureImageAsync(captureReq, ct).ConfigureAwait(false);
      if (!image.Ok)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(warning: image.Meta.Warning),
          Image: image,
          SnapshotId: "",
          Elements: Array.Empty<UiaElement>(),
          Error: image.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Capture failed."));
      }

      var snapshot = await UiaSnapshotAsync(
        new UiaSnapshotRequest(
          Target: req.Target,
          Depth: req.Depth,
          MaxNodes: req.MaxNodes,
          IncludeProperties: req.IncludeProperties),
        ct).ConfigureAwait(false);

      if (!snapshot.Ok)
      {
        return new SeeResult(
          Ok: false,
          Meta: scope.Meta(warning: CombineWarnings(image.Meta.Warning, snapshot.Meta.Warning)),
          Image: image,
          SnapshotId: snapshot.SnapshotId,
          Elements: snapshot.Elements,
          Error: snapshot.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "UIA snapshot failed."));
      }

      return new SeeResult(
        Ok: true,
        Meta: scope.Meta(warning: CombineWarnings(image.Meta.Warning, snapshot.Meta.Warning)),
        Image: image,
        SnapshotId: snapshot.SnapshotId,
        Elements: snapshot.Elements);
    }
    catch (OperationCanceledException)
    {
      return new SeeResult(
        Ok: false,
        Meta: scope.Meta(),
        Image: new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled.")),
        SnapshotId: "",
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new SeeResult(
        Ok: false,
        Meta: scope.Meta(),
        Image: new CaptureImageResult(
          Ok: false,
          Meta: scope.Meta(),
          ImagePath: "",
          MimeType: "image/png",
          Width: 0,
          Height: 0,
          Error: PeekuErrors.Create(PeekuErrorCode.Internal, "See failed.", new { exception = ex.GetType().FullName, ex.Message, ex.HResult })),
        SnapshotId: "",
        Elements: Array.Empty<UiaElement>(),
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "See failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }
}
