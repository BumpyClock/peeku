using System.Collections.Concurrent;
using peeku;

namespace peeku.Daemon;

/// <summary>
/// Owns a single-client daemon session with a dedicated actor thread for serialized UI work.
/// </summary>
/// <example>
/// <code>
/// using var session = new DaemonSession();
/// var result = await session.ExecuteAsync((client, ct) => client.WindowsFocusedAsync(ct), CancellationToken.None);
/// </code>
/// </example>
public sealed class DaemonSession : IDisposable
{
  private readonly IPeekuClient _client;
  private readonly BlockingCollection<WorkItem> _queue;
  private readonly CancellationTokenSource _shutdown;
  private readonly Thread _actorThread;
  private int _disposed;

  public DaemonSession()
    : this(() => new DaemonPeekuClient())
  {
  }

  internal DaemonSession(Func<IPeekuClient> clientFactory)
  {
    if (clientFactory is null)
    {
      throw new ArgumentNullException(nameof(clientFactory));
    }

    _client = clientFactory() ?? throw new InvalidOperationException("Client factory returned null");
    _queue = new BlockingCollection<WorkItem>(new ConcurrentQueue<WorkItem>());
    _shutdown = new CancellationTokenSource();
    _actorThread = new Thread(RunLoop)
    {
      IsBackground = true,
      Name = "peeku-daemon-session",
    };
    _actorThread.Start();
  }

  public Task<T> ExecuteAsync<T>(Func<IPeekuClient, CancellationToken, Task<T>> work, CancellationToken ct)
  {
    if (work is null)
    {
      throw new ArgumentNullException(nameof(work));
    }

    if (ct.IsCancellationRequested)
    {
      return Task.FromCanceled<T>(ct);
    }

    ThrowIfDisposed();

    var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
    var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    void Cancel()
    {
      if (!linkedCts.IsCancellationRequested)
      {
        linkedCts.Cancel();
      }

      tcs.TrySetCanceled();
      linkedCts.Dispose();
    }

    void Execute(IPeekuClient client)
    {
      try
      {
        var task = work(client, linkedCts.Token);
        if (task is null)
        {
          throw new InvalidOperationException($"Daemon session work returned null task for {typeof(T).FullName}");
        }

        var result = task.GetAwaiter().GetResult();
        if (result is null)
        {
          throw new InvalidOperationException($"Daemon session work returned null for {typeof(T).FullName}");
        }

        tcs.TrySetResult(result);
      }
      catch (OperationCanceledException ex)
      {
        if (ex.CancellationToken.CanBeCanceled)
        {
          tcs.TrySetCanceled(ex.CancellationToken);
        }
        else
        {
          tcs.TrySetCanceled();
        }
      }
      catch (Exception ex)
      {
        tcs.TrySetException(ex);
      }
      finally
      {
        linkedCts.Dispose();
      }
    }

    var item = new WorkItem(Execute, Cancel);
    if (!_queue.TryAdd(item))
    {
      Cancel();
      throw new ObjectDisposedException(nameof(DaemonSession), "Session rejected new work after shutdown");
    }

    return tcs.Task;
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 1)
    {
      return;
    }

    _shutdown.Cancel();
    _queue.CompleteAdding();

    if (!ReferenceEquals(Thread.CurrentThread, _actorThread))
    {
      _actorThread.Join();
    }

    _shutdown.Dispose();
    _queue.Dispose();
  }

  private void RunLoop()
  {
    try
    {
      foreach (var item in _queue.GetConsumingEnumerable(_shutdown.Token))
      {
        item.Run(_client);
      }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
      while (_queue.TryTake(out var item))
      {
        item.Cancel();
      }

      if (_client is IDisposable disposable)
      {
        disposable.Dispose();
      }
    }
  }

  private void ThrowIfDisposed()
  {
    if (Volatile.Read(ref _disposed) == 1)
    {
      throw new ObjectDisposedException(nameof(DaemonSession), "Session is closed");
    }
  }

  private readonly struct WorkItem
  {
    public WorkItem(Action<IPeekuClient> run, Action cancel)
    {
      _run = run ?? throw new ArgumentNullException(nameof(run));
      _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
    }

    private readonly Action<IPeekuClient> _run;
    private readonly Action _cancel;

    public void Run(IPeekuClient client) => _run(client);

    public void Cancel() => _cancel();
  }
}
