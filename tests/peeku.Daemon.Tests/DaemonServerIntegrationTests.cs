using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using peeku.Daemon;
using Xunit;

namespace peeku.Daemon.Tests;

public sealed class DaemonServerIntegrationTests
{
  [Fact]
  public async Task Server_ping_batch_and_shutdown_work_over_named_pipe()
  {
    var pipeName = $"peeku.test.{Guid.NewGuid():N}";
    var shutdown = new DaemonShutdown();
    var options = CreateJsonOptions();
    var codec = new JsonRpcCodec(options);
    var dispatcher = new JsonRpcDispatcher(options);
    using var session = new DaemonSession();
    var connection = new JsonRpcConnection(codec, dispatcher, session, shutdown);
    var server = new DaemonServer(pipeName, connection, shutdown);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var serverTask = Task.Run(() => server.RunAsync(cts.Token), cts.Token);

    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(cts.Token);

    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
    {
      AutoFlush = true,
      NewLine = "\n",
    };

    await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":"1","method":"server.ping"}""");
    var ping = await ReadJsonAsync(reader, cts.Token);
    Assert.Equal("2.0", ping.RootElement.GetProperty("jsonrpc").GetString());
    Assert.Equal("1", ping.RootElement.GetProperty("id").GetString());
    Assert.True(ping.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());

    await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":"2","method":"peeku.batch","params":{"ops":[{"tool":"unknown_tool","args":{}}],"stopOnError":true}}""");
    var batch = await ReadJsonAsync(reader, cts.Token);
    Assert.Equal("2", batch.RootElement.GetProperty("id").GetString());
    Assert.False(batch.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
    var step = batch.RootElement.GetProperty("result").GetProperty("results")[0];
    Assert.Equal("unknown_tool", step.GetProperty("tool").GetString());
    Assert.False(step.GetProperty("ok").GetBoolean());
    Assert.Equal("NotSupported", step.GetProperty("error").GetProperty("code").GetString());

    await writer.WriteLineAsync("""{"jsonrpc":"2.0","id":"3","method":"server.shutdown"}""");
    var stop = await ReadJsonAsync(reader, cts.Token);
    Assert.Equal("3", stop.RootElement.GetProperty("id").GetString());
    Assert.True(stop.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());

    await serverTask.WaitAsync(cts.Token);
    Assert.True(shutdown.IsRequested);
  }

  private static async Task<JsonDocument> ReadJsonAsync(StreamReader reader, CancellationToken ct)
  {
    var line = await reader.ReadLineAsync().WaitAsync(ct);
    Assert.False(string.IsNullOrWhiteSpace(line));
    return JsonDocument.Parse(line!);
  }

  private static JsonSerializerOptions CreateJsonOptions()
    => new()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };
}
