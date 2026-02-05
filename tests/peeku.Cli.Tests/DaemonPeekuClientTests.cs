using System.Text.Json;
using peeku;
using peeku.Cli;
using Xunit;

namespace peeku.Cli.Tests;

/// <summary>
/// Daemon client mapping tests.
/// </summary>
/// <example>
/// <code>
/// var client = new DaemonPeekuClient(new FakeDaemonJsonRpcClient(_ => new BatchResult(true, Results.Start("t").Meta(), Array.Empty&lt;BatchStepResult&gt;())));
/// </code>
/// </example>
public sealed class DaemonPeekuClientTests
{
  [Fact]
  public async Task The_windows_list_call_maps_to_a_batch_tool_request()
  {
    var meta = Results.Start("test").Meta();
    var listResult = new WindowListResult(
      Ok: true,
      Meta: meta,
      Windows: new[] { new WindowInfo("0x0000000000000001", 42, "Title", "proc") });

    var rpc = new FakeDaemonJsonRpcClient(batch =>
    {
      var payload = JsonSerializer.SerializeToElement(listResult);
      var step = new BatchStepResult("peeku_windows_list", true, 1, payload, null);
      return new BatchResult(true, meta, new[] { step }, null);
    });

    var client = new DaemonPeekuClient(rpc);

    var response = await client.WindowsListAsync(new WindowsListRequest("note", "proc", 5), CancellationToken.None);

    Assert.True(response.Ok);
    Assert.Single(response.Windows);
    Assert.Equal("Title", response.Windows[0].Title);

    Assert.Equal("peeku.batch", rpc.LastMethod);
    var batch = Assert.IsType<BatchRequest>(rpc.LastParams);
    Assert.Single(batch.Ops);
    Assert.Equal("peeku_windows_list", batch.Ops[0].Tool);
    var args = batch.Ops[0].Args;
    Assert.Equal(JsonValueKind.Object, args.ValueKind);
    Assert.Equal("note", args.GetProperty("titleContains").GetString());
    Assert.Equal("proc", args.GetProperty("processName").GetString());
    Assert.Equal(5, args.GetProperty("limit").GetInt32());
  }

  /// <summary>
  /// Fake JSON-RPC client for daemon client tests.
  /// </summary>
  /// <example>
  /// <code>
  /// var fake = new FakeDaemonJsonRpcClient(_ => new BatchResult(true, Results.Start("t").Meta(), Array.Empty&lt;BatchStepResult&gt;()));
  /// </code>
  /// </example>
  private sealed class FakeDaemonJsonRpcClient : IDaemonJsonRpcClient
  {
    private readonly Func<BatchRequest, BatchResult> _resultFactory;

    internal FakeDaemonJsonRpcClient(Func<BatchRequest, BatchResult> resultFactory)
    {
      _resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
    }

    internal string LastMethod { get; private set; } = "";
    internal object LastParams { get; private set; } = new object();

    public Task<T> CallAsync<T>(string method, object parameters, CancellationToken ct)
    {
      if (string.IsNullOrWhiteSpace(method))
      {
        throw new ArgumentException("Method is required", nameof(method));
      }

      if (parameters is not BatchRequest batch)
      {
        throw new InvalidOperationException("Expected BatchRequest parameters");
      }

      if (typeof(T) != typeof(BatchResult))
      {
        throw new InvalidOperationException("Expected BatchResult response type");
      }

      LastMethod = method;
      LastParams = batch;

      var result = _resultFactory(batch);
      return Task.FromResult((T)(object)result);
    }
  }
}
