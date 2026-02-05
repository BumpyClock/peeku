using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace peeku.Mcp;

public static class PeekuMcpHandlers
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
  };

  public static ValueTask<ListToolsResult> ListTools(RequestContext<ListToolsRequestParams> context, CancellationToken ct)
    => ValueTask.FromResult(new ListToolsResult { Tools = PeekuMcpToolCatalog.Tools });

  public static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    var name = context.Params?.Name ?? "";
    var args = PeekuMcpJson.ToArgsElement(context.Params?.Arguments);

    var services = context.Services ?? throw new InvalidOperationException("Request services unavailable.");
    var client = services.GetRequiredService<IPeekuClient>();
    var (ok, meta, payload, error) = await PeekuMcpToolDispatcher.DispatchAsync(client, name, args, ct).ConfigureAwait(false);

    var response = PeekuMcpResponse.Build(name, ok, meta, payload, error);
    var node = JsonSerializer.SerializeToNode(response, JsonOptions);

    var blocks = BuildContentBlocks(node, payload);

    return new CallToolResult
    {
      IsError = !ok,
      StructuredContent = node,
      Content = blocks.Count == 0 ? Array.Empty<ContentBlock>() : blocks.ToArray(),
    };
  }

  internal static List<ContentBlock> BuildContentBlocks(JsonNode? structuredContent, object? payload)
  {
    var blocks = new List<ContentBlock>();
    if (structuredContent is not null)
    {
      blocks.Add(new TextContentBlock { Text = structuredContent.ToJsonString(JsonOptions) });
    }

    if (TryGetPng(payload, out var base64Png, out var mimeType))
    {
      blocks.Add(new ImageContentBlock { Data = base64Png, MimeType = mimeType });
    }

    return blocks;
  }

  private static bool TryGetPng(object? payload, out string base64Png, out string mimeType)
  {
    if (payload is CaptureImageResult capture && !string.IsNullOrWhiteSpace(capture.Base64Png))
    {
      base64Png = capture.Base64Png;
      mimeType = string.IsNullOrWhiteSpace(capture.MimeType) ? "image/png" : capture.MimeType;
      return true;
    }

    if (payload is SeeResult see && !string.IsNullOrWhiteSpace(see.Image.Base64Png))
    {
      base64Png = see.Image.Base64Png!;
      mimeType = string.IsNullOrWhiteSpace(see.Image.MimeType) ? "image/png" : see.Image.MimeType;
      return true;
    }

    base64Png = "";
    mimeType = "image/png";
    return false;
  }
}
