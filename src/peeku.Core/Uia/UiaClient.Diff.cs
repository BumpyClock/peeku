namespace peeku;

public sealed partial class UiaClient
{
  public async Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default)
  {
    var scope = Results.Start();
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new DiffResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotIdBefore: null,
          SnapshotIdAfter: null,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      var snapReq = new UiaSnapshotRequest(
        Target: req.TargetBefore,
        Depth: req.Depth,
        MaxNodes: req.MaxNodes,
        IncludeProperties: req.IncludeProperties);

      var before = await UiaSnapshotAsync(snapReq, ct).ConfigureAwait(false);
      if (!before.Ok)
      {
        return new DiffResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotIdBefore: null,
          SnapshotIdAfter: null,
          Error: before.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "Before-snapshot failed."));
      }

      var snapReqAfter = new UiaSnapshotRequest(
        Target: req.TargetAfter,
        Depth: req.Depth,
        MaxNodes: req.MaxNodes,
        IncludeProperties: req.IncludeProperties);

      var after = await UiaSnapshotAsync(snapReqAfter, ct).ConfigureAwait(false);
      if (!after.Ok)
      {
        return new DiffResult(
          Ok: false,
          Meta: scope.Meta(),
          SnapshotIdBefore: before.SnapshotId,
          SnapshotIdAfter: null,
          Error: after.Error ?? PeekuErrors.Create(PeekuErrorCode.Internal, "After-snapshot failed."));
      }

      var delta = UiaTreeDiff.Diff(before, after);

      var warning = CombineWarnings(before.Meta.Warning, after.Meta.Warning);
      return new DiffResult(
        Ok: true,
        Meta: scope.Meta(warning: warning),
        SnapshotIdBefore: before.SnapshotId,
        SnapshotIdAfter: after.SnapshotId,
        Delta: delta);
    }
    catch (OperationCanceledException)
    {
      return new DiffResult(
        Ok: false,
        Meta: scope.Meta(),
        SnapshotIdBefore: null,
        SnapshotIdAfter: null,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new DiffResult(
        Ok: false,
        Meta: scope.Meta(),
        SnapshotIdBefore: null,
        SnapshotIdAfter: null,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Diff failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }
}
