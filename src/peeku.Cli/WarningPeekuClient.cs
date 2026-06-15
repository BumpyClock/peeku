using peeku;

namespace peeku.Cli;

/// <summary>
/// IPeekuClient decorator that injects a warning into ResultMeta for all result-returning methods.
/// Example: <code>var client = new WarningPeekuClient(inner, "Daemon unreachable");</code>
/// </summary>
internal sealed class WarningPeekuClient : IPeekuClient
{
  private readonly IPeekuClient _inner;
  private readonly string _warning;

  internal WarningPeekuClient(IPeekuClient inner, string warning)
  {
    _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    _warning = string.IsNullOrWhiteSpace(warning) ? "Warning" : warning.Trim();
  }

  public async Task<DoctorResult> DoctorAsync(DoctorRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.DoctorAsync(req, ct).ConfigureAwait(false));

  public async Task<WindowListResult> WindowsListAsync(WindowsListRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowsListAsync(req, ct).ConfigureAwait(false));

  public async Task<FocusedWindowResult> WindowsFocusedAsync(CancellationToken ct = default)
    => WithWarning(await _inner.WindowsFocusedAsync(ct).ConfigureAwait(false));

  public async Task<FocusedWindowResult> WindowFocusAsync(WindowFocusRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowFocusAsync(req, ct).ConfigureAwait(false));

  public async Task<CaptureImageResult> CaptureImageAsync(CaptureImageRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.CaptureImageAsync(req, ct).ConfigureAwait(false));

  public async Task<UiaSnapshotResult> UiaSnapshotAsync(UiaSnapshotRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.UiaSnapshotAsync(req, ct).ConfigureAwait(false));

  public async Task<SeeResult> SeeAsync(SeeRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.SeeAsync(req, ct).ConfigureAwait(false));

  public async Task<FindResult> FindAsync(FindRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.FindAsync(req, ct).ConfigureAwait(false));

  public async Task<ElementGetResult> ElementGetAsync(ElementGetRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.ElementGetAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> ClickAsync(ClickRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.ClickAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> InvokeAsync(InvokeRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.InvokeAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> SetValueAsync(SetValueRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.SetValueAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> TypeAsync(TypeRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.TypeAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> ScrollAsync(ScrollRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.ScrollAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> HotkeyAsync(HotkeyRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.HotkeyAsync(req, ct).ConfigureAwait(false));

  public async Task<ActionResult> PressAsync(PressRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.PressAsync(req, ct).ConfigureAwait(false));

  public IAsyncEnumerable<ObservationEvent> ObserveAsync(ObserveRequest req, CancellationToken ct = default)
    => _inner.ObserveAsync(req, ct);

  public async Task<WaitResult> WaitAsync(WaitRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WaitAsync(req, ct).ConfigureAwait(false));

  public async Task<BatchResult> BatchAsync(BatchRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.BatchAsync(req, ct).ConfigureAwait(false));

  public async Task<DiffResult> DiffAsync(DiffRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.DiffAsync(req, ct).ConfigureAwait(false));
  public async Task<ElementAtPointResult> ElementAtPointAsync(ElementAtPointRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.ElementAtPointAsync(req, ct).ConfigureAwait(false));

  public async Task<WindowActionResult> WindowMoveAsync(WindowMoveRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowMoveAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowResizeAsync(WindowResizeRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowResizeAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowSetBoundsAsync(WindowBoundsRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowSetBoundsAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowMinimizeAsync(WindowStateRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowMinimizeAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowMaximizeAsync(WindowStateRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowMaximizeAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowRestoreAsync(WindowStateRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowRestoreAsync(req, ct).ConfigureAwait(false));
  public async Task<WindowActionResult> WindowCloseAsync(WindowCloseRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.WindowCloseAsync(req, ct).ConfigureAwait(false));

  public async Task<AppLaunchResult> AppLaunchAsync(AppLaunchRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.AppLaunchAsync(req, ct).ConfigureAwait(false));
  public async Task<AppQuitResult> AppQuitAsync(AppQuitRequest req, CancellationToken ct = default)
    => WithWarning(await _inner.AppQuitAsync(req, ct).ConfigureAwait(false));

  private T WithWarning<T>(T result) where T : ResultBase
  {
    var existing = result.Meta.Warning ?? "";
    var combined = CombineWarnings(existing, _warning);
    var warning = string.IsNullOrWhiteSpace(combined) ? null : combined;
    var current = result.Meta.Warning ?? "";
    var next = warning ?? "";
    if (string.Equals(current, next, StringComparison.Ordinal))
    {
      return result;
    }

    return result with { Meta = result.Meta with { Warning = warning } };
  }

  private static string CombineWarnings(string existing, string added)
  {
    if (string.IsNullOrWhiteSpace(existing))
    {
      return added;
    }

    if (string.IsNullOrWhiteSpace(added))
    {
      return existing;
    }

    return $"{existing} {added}";
  }
}
