using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace peeku.Cli;

/// <summary>
/// JSON-RPC client abstraction for daemon calls with a single request-response pipeline.
/// Example: <code>await client.CallAsync&lt;DaemonOkResult&gt;("server.ping", new Dictionary&lt;string, object?&gt;(), ct);</code>
/// </summary>
internal interface IDaemonJsonRpcClient
{
  Task<T> CallAsync<T>(string method, object parameters, CancellationToken ct);
}

/// <summary>
/// JSON-RPC client over a named pipe using JSONL framing for daemon communication.
/// Example: <code>var client = new DaemonJsonRpcClient("peeku.user.v1", TimeSpan.FromSeconds(2));</code>
/// </summary>
internal sealed class DaemonJsonRpcClient : IDaemonJsonRpcClient
{
  private readonly string _pipeName;
  private readonly TimeSpan _connectTimeout;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly JsonSerializerOptions _jsonOptions;
  private long _nextId;
  private NamedPipeClientStream? _pipe;
  private StreamReader? _reader;
  private StreamWriter? _writer;

  internal DaemonJsonRpcClient(string pipeName, TimeSpan connectTimeout)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      throw new ArgumentException("Pipe name is required", nameof(pipeName));
    }

    _pipeName = pipeName.Trim();
    _connectTimeout = connectTimeout;
    _jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true,
      WriteIndented = false,
    };
  }

  internal async Task<bool> TryPingAsync(CancellationToken ct)
  {
    try
    {
      var res = await CallAsync<DaemonOkResult>("server.ping", new Dictionary<string, object?>(), ct).ConfigureAwait(false);
      return res.Ok;
    }
    catch
    {
      return false;
    }
  }

  public async Task<T> CallAsync<T>(string method, object parameters, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(method))
    {
      throw new ArgumentException("Method is required", nameof(method));
    }

    if (parameters is null)
    {
      throw new ArgumentNullException(nameof(parameters));
    }

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      await EnsureConnectedAsync(ct).ConfigureAwait(false);

      var id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
      var paramsElement = JsonSerializer.SerializeToElement(parameters, _jsonOptions);
      var request = new JsonRpcRequest("2.0", id, method.Trim(), paramsElement);
      var payload = JsonSerializer.Serialize(request, _jsonOptions);

      await WriteLineAsync(payload, ct).ConfigureAwait(false);

      while (true)
      {
        var line = await ReadLineAsync(ct).ConfigureAwait(false);
        if (line.Length == 0)
        {
          continue;
        }

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!TryReadId(root, out var responseId))
        {
          continue;
        }

        if (!string.Equals(responseId, id, StringComparison.Ordinal))
        {
          continue;
        }

        if (TryReadError(root, out var error))
        {
          throw new InvalidOperationException($"JSON-RPC error for '{method}' code {error.Code}: {error.Message}");
        }

        if (!root.TryGetProperty("result", out var resultEl))
        {
          throw new InvalidOperationException($"JSON-RPC response for '{method}' missing result");
        }

        var result = resultEl.Deserialize<T>(_jsonOptions);
        if (result is null)
        {
          throw new InvalidOperationException($"JSON-RPC response for '{method}' returned empty result");
        }

        return result;
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task EnsureConnectedAsync(CancellationToken ct)
  {
    if (_pipe is not null && _pipe.IsConnected)
    {
      return;
    }

    DisposePipe();

    var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (_connectTimeout > TimeSpan.Zero)
    {
      cts.CancelAfter(_connectTimeout);
    }

    await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

    _pipe = pipe;
    _reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, true);
    _writer = new StreamWriter(pipe, new UTF8Encoding(false))
    {
      AutoFlush = false,
      NewLine = "\n",
    };
  }

  private async Task<string> ReadLineAsync(CancellationToken ct)
  {
    var reader = _reader ?? throw new InvalidOperationException("JSON-RPC reader not initialized");
    var line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
    if (line is null)
    {
      throw new InvalidOperationException("Daemon pipe closed");
    }

    return line.Trim();
  }

  private async Task WriteLineAsync(string line, CancellationToken ct)
  {
    var writer = _writer ?? throw new InvalidOperationException("JSON-RPC writer not initialized");
    await writer.WriteLineAsync(line).WaitAsync(ct).ConfigureAwait(false);
    await writer.FlushAsync(ct).ConfigureAwait(false);
  }

  private void DisposePipe()
  {
    _writer?.Dispose();
    _reader?.Dispose();
    _pipe?.Dispose();
    _writer = null;
    _reader = null;
    _pipe = null;
  }

  private static bool TryReadId(JsonElement root, out string id)
  {
    id = "";
    if (!root.TryGetProperty("id", out var idEl))
    {
      return false;
    }

    if (idEl.ValueKind == JsonValueKind.String)
    {
      id = idEl.GetString() ?? "";
      return id.Length > 0;
    }

    if (idEl.ValueKind == JsonValueKind.Number)
    {
      id = idEl.GetRawText();
      return id.Length > 0;
    }

    return false;
  }

  private static bool TryReadError(JsonElement root, out JsonRpcError error)
  {
    error = new JsonRpcError(-32603, "Internal error");
    if (!root.TryGetProperty("error", out var errEl))
    {
      return false;
    }

    if (errEl.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    var code = -32603;
    if (errEl.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var codeVal))
    {
      code = codeVal;
    }

    var message = "JSON-RPC error";
    if (errEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
    {
      message = msgEl.GetString() ?? "JSON-RPC error";
    }

    error = new JsonRpcError(code, message);
    return true;
  }

  /// <summary>
  /// JSON-RPC 2.0 request envelope used by the daemon client.
  /// Example: <code>var req = new JsonRpcRequest("2.0", "1", "server.ping", JsonDocument.Parse("{}").RootElement);</code>
  /// </summary>
  private sealed record JsonRpcRequest(
    string Jsonrpc,
    string Id,
    string Method,
    JsonElement Params);

  /// <summary>
  /// JSON-RPC error payload used for daemon error handling.
  /// Example: <code>var err = new JsonRpcError(-32601, "Method not found");</code>
  /// </summary>
  private sealed record JsonRpcError(
    int Code,
    string Message);
}

/// <summary>
/// Minimal ok+meta response model used for daemon ping and shutdown calls.
/// Example: <code>var ok = await client.CallAsync&lt;DaemonOkResult&gt;("server.shutdown", new Dictionary&lt;string, object?&gt;(), ct);</code>
/// </summary>
internal sealed record DaemonOkResult(
  bool Ok,
  ResultMeta Meta,
  PeekuError? Error = null) : ResultBase(Ok, Meta, Error);
