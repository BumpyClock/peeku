using System.Text.Json;
using System.Text.Json.Serialization;
using peeku;
using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

/// <summary>
/// JSON-RPC connection tests over in-memory streams.
/// </summary>
/// <example>
/// <code>
/// var shutdown = new DaemonShutdown();
/// var connection = new JsonRpcConnection(new JsonRpcCodec(new JsonSerializerOptions()), new JsonRpcDispatcher(new JsonSerializerOptions()), shutdown);
/// await connection.ProcessAsync(new StringReader("{}"), new StringWriter(), CancellationToken.None);
/// </code>
/// </example>
public sealed class JsonRpcConnectionTests
{
  [Fact]
  public async Task The_server_ping_returns_an_ok_response()
  {
    var shutdown = new DaemonShutdown();
    var connection = CreateConnection(shutdown);
    var reader = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"server.ping\"}");
    var writer = new StringWriter();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

    await connection.ProcessAsync(reader, writer, cts.Token);

    var output = writer.ToString().Trim();
    using var doc = JsonDocument.Parse(output);
    var root = doc.RootElement;
    Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
    Assert.Equal("1", root.GetProperty("id").GetString());
    Assert.True(root.GetProperty("result").GetProperty("ok").GetBoolean());
  }

  [Fact]
  public async Task An_unknown_method_returns_a_method_not_found_error()
  {
    var shutdown = new DaemonShutdown();
    var connection = CreateConnection(shutdown);
    var reader = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"server.nope\"}");
    var writer = new StringWriter();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

    await connection.ProcessAsync(reader, writer, cts.Token);

    var output = writer.ToString().Trim();
    using var doc = JsonDocument.Parse(output);
    var root = doc.RootElement;
    Assert.Equal(-32601, root.GetProperty("error").GetProperty("code").GetInt32());
  }

  [Fact]
  public async Task A_batch_with_invalid_params_returns_an_invalid_params_error()
  {
    var shutdown = new DaemonShutdown();
    var connection = CreateConnection(shutdown);
    var reader = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"peeku.batch\",\"params\":123}");
    var writer = new StringWriter();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

    await connection.ProcessAsync(reader, writer, cts.Token);

    var output = writer.ToString().Trim();
    using var doc = JsonDocument.Parse(output);
    var root = doc.RootElement;
    Assert.Equal(-32602, root.GetProperty("error").GetProperty("code").GetInt32());
  }

  [Fact]
  public async Task A_notification_does_not_write_a_response()
  {
    var shutdown = new DaemonShutdown();
    var connection = CreateConnection(shutdown);
    var reader = new StringReader("{\"jsonrpc\":\"2.0\",\"method\":\"server.ping\"}");
    var writer = new StringWriter();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

    await connection.ProcessAsync(reader, writer, cts.Token);

    Assert.True(string.IsNullOrWhiteSpace(writer.ToString()));
  }

  private JsonRpcConnection CreateConnection(DaemonShutdown shutdown)
  {
    var options = CreateJsonOptions();
    var codec = new JsonRpcCodec(options);
    var dispatcher = new JsonRpcDispatcher(options);
    return new JsonRpcConnection(codec, dispatcher, shutdown);
  }

  private JsonSerializerOptions CreateJsonOptions()
    => new()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };
}
