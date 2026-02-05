using System.Threading.Channels;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace peeku;

internal static class UiaLiveWait
{
  internal readonly record struct RootResolution(
    AutomationElement? Root,
    string? Warning);

  private static readonly TimeSpan DefaultPollFallback = TimeSpan.FromSeconds(1);
  private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(50);

  internal static Task<WaitResult> WaitAsync(
    WaitRequest req,
    Func<Target, UIA3Automation, CancellationToken, RootResolution> resolveRoot,
    CancellationToken ct)
    => WaitAsync(req, resolveRoot, DefaultPollFallback, DefaultDebounce, ct);

  internal static async Task<WaitResult> WaitAsync(
    WaitRequest req,
    Func<Target, UIA3Automation, CancellationToken, RootResolution> resolveRoot,
    TimeSpan pollFallback,
    TimeSpan debounce,
    CancellationToken ct)
  {
    var scope = Results.Start();
    string? warning = null;
    try
    {
      ct.ThrowIfCancellationRequested();

      if (req is null)
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Request is required."));
      }

      if (req.Selector is null || string.IsNullOrWhiteSpace(req.Selector.Expr))
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Selector is required."));
      }

      if (resolveRoot is null)
      {
        return new WaitResult(
          Ok: false,
          Meta: scope.Meta(),
          Found: false,
          Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "ResolveRoot function is required."));
      }

      var timeout = req.Timeout;
      if (timeout < TimeSpan.Zero)
      {
        timeout = TimeSpan.Zero;
      }

      var poll = pollFallback <= TimeSpan.Zero ? DefaultPollFallback : pollFallback;
      var quiet = debounce < TimeSpan.Zero ? DefaultDebounce : debounce;

      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      if (timeout > TimeSpan.Zero)
      {
        timeoutCts.CancelAfter(timeout);
      }

      var token = timeoutCts.Token;

      using var automation = new UIA3Automation();

      var channel = Channel.CreateBounded<int>(
        new BoundedChannelOptions(capacity: 512)
        {
          SingleReader = true,
          SingleWriter = false,
          FullMode = BoundedChannelFullMode.DropOldest,
        });

      void Signal()
        => _ = channel.Writer.TryWrite(0);

      Func<Task> signalFactory = quiet <= TimeSpan.Zero
        ? () => WaitForSignalAsync(channel.Reader, token)
        : () => WaitForDebouncedSignalAsync(channel.Reader, quiet, token);

      async Task<WaitResult> RunLoopAsync()
      {
        var debouncedSignal = signalFactory();

        while (true)
        {
          token.ThrowIfCancellationRequested();

          var targetUsed = req.Target ?? Target.Focused();
          var resolution = resolveRoot(targetUsed, automation, token);
          warning = CombineWarnings(warning, resolution.Warning);

          if (resolution.Root is null)
          {
            return new WaitResult(
              Ok: false,
              Meta: scope.Meta(warning: warning),
              Found: false,
              Error: PeekuErrors.Create(PeekuErrorCode.WindowNotFound, "Target window not found."));
          }

          AutomationElement? match;
          try
          {
            var matches = UiaLiveSelectors.Select(resolution.Root, req.Selector, limit: 1, token);
            match = matches.Count > 0 ? matches[0] : null;
          }
          catch (ArgumentException ex)
          {
            return new WaitResult(
              Ok: false,
              Meta: scope.Meta(warning: warning),
              Found: false,
              Error: PeekuErrors.Create(PeekuErrorCode.InvalidArgument, "Invalid selector.", new { selector = req.Selector.Expr, error = ex.Message }));
          }

          if (match is not null)
          {
            string refId;
            try
            {
              refId = UiaRefId.Create(match);
            }
            catch (Exception ex)
            {
              return new WaitResult(
                Ok: false,
                Meta: scope.Meta(warning: warning),
                Found: false,
                Error: PeekuErrors.Create(
                  PeekuErrorCode.Internal,
                  "Failed to compute element refId.",
                  new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
            }

            return new WaitResult(
              Ok: true,
              Meta: scope.Meta(warning: warning),
              Found: true,
              Element: new ElementRef(refId));
          }

          if (timeout <= TimeSpan.Zero)
          {
            return new WaitResult(
              Ok: true,
              Meta: scope.Meta(warning: warning),
              Found: false);
          }

          var pollTask = Task.Delay(poll, token);
          var completed = await Task.WhenAny(debouncedSignal, pollTask).ConfigureAwait(false);

          if (completed == debouncedSignal)
          {
            debouncedSignal = signalFactory();
          }
        }
      }

      try
      {
        var desktop = automation.GetDesktop();

        using var _structure = desktop.RegisterStructureChangedEvent(TreeScope.Subtree, (_, _, _) => Signal());

        using var _property = desktop.RegisterPropertyChangedEvent(
          TreeScope.Subtree,
          (_, _, _) => Signal(),
          automation.PropertyLibrary.Element.Name,
          automation.PropertyLibrary.Element.IsOffscreen);

        using var _focus = automation.RegisterFocusChangedEvent(_ => Signal());

        return await RunLoopAsync().ConfigureAwait(false);
      }
      catch
      {
        warning = "UIA events unavailable; using polling fallback";
        return await RunLoopAsync().ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException)
    {
      if (!ct.IsCancellationRequested)
      {
        return new WaitResult(
          Ok: true,
          Meta: scope.Meta(warning: CombineWarnings(warning, "Timed out waiting for selector.")),
          Found: false);
      }

      return new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(PeekuErrorCode.Canceled, "Operation cancelled."));
    }
    catch (Exception ex)
    {
      return new WaitResult(
        Ok: false,
        Meta: scope.Meta(),
        Found: false,
        Error: PeekuErrors.Create(
          PeekuErrorCode.Internal,
          "Wait failed.",
          new { exception = ex.GetType().FullName, ex.Message, ex.HResult }));
    }
  }

  private static async Task WaitForSignalAsync(ChannelReader<int> reader, CancellationToken ct)
  {
    _ = await reader.WaitToReadAsync(ct).ConfigureAwait(false);
    Drain(reader);
  }

  private static async Task WaitForDebouncedSignalAsync(ChannelReader<int> reader, TimeSpan debounce, CancellationToken ct)
  {
    _ = await reader.WaitToReadAsync(ct).ConfigureAwait(false);
    Drain(reader);

    using var quietCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    quietCts.CancelAfter(debounce);

    while (true)
    {
      try
      {
        _ = await reader.WaitToReadAsync(quietCts.Token).ConfigureAwait(false);
        Drain(reader);
        quietCts.CancelAfter(debounce);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        return;
      }
    }
  }

  private static void Drain(ChannelReader<int> reader)
  {
    while (reader.TryRead(out _))
    {
    }
  }

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
}
