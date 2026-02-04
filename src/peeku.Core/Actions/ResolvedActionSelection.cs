namespace peeku;

internal sealed record ResolvedActionSelection(
  Target Target,
  string SnapshotId,
  ElementRef Element,
  Rect Rect);

