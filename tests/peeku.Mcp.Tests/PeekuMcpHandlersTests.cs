using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using peeku;
using peeku.Mcp;
using Xunit;

namespace peeku.Mcp.Tests;

public sealed class PeekuMcpHandlersTests
{
  [Fact]
  public void BuildContentBlocks_WhenCaptureHasBase64_AddsImageBlock()
  {
    var meta = new ResultMeta("t1", DateTimeOffset.UnixEpoch, 1);
    var capture = new CaptureImageResult(
      Ok: true,
      Meta: meta,
      ImagePath: "x.png",
      MimeType: "image/png",
      Width: 1,
      Height: 1,
      Base64Png: "AAA");

    var structured = new JsonObject { ["ok"] = true, ["base64Png"] = "AAA" };
    var blocks = PeekuMcpHandlers.BuildContentBlocks(structured, capture);

    Assert.Equal(2, blocks.Count);

    var text = Assert.IsType<TextContentBlock>(blocks[0]);
    using var doc = JsonDocument.Parse(text.Text);
    Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    Assert.Equal("AAA", doc.RootElement.GetProperty("base64Png").GetString());

    var image = Assert.IsType<ImageContentBlock>(blocks[1]);
    Assert.Equal("AAA", image.Data);
    Assert.Equal("image/png", image.MimeType);
  }

  [Fact]
  public void BuildContentBlocks_WhenCaptureMimeBlank_DefaultsToPng()
  {
    var meta = new ResultMeta("t2", DateTimeOffset.UnixEpoch, 1);
    var capture = new CaptureImageResult(
      Ok: true,
      Meta: meta,
      ImagePath: "x.png",
      MimeType: "",
      Width: 1,
      Height: 1,
      Base64Png: "AAA");

    var blocks = PeekuMcpHandlers.BuildContentBlocks(new JsonObject { ["ok"] = true }, capture);
    var image = Assert.IsType<ImageContentBlock>(blocks[1]);
    Assert.Equal("image/png", image.MimeType);
  }

  [Fact]
  public void BuildContentBlocks_WhenSeeHasBase64_AddsImageBlock()
  {
    var meta = new ResultMeta("t3", DateTimeOffset.UnixEpoch, 1);
    var imageRes = new CaptureImageResult(
      Ok: true,
      Meta: meta,
      ImagePath: "x.png",
      MimeType: "image/png",
      Width: 1,
      Height: 1,
      Base64Png: "AAA");

    var see = new SeeResult(
      Ok: true,
      Meta: meta,
      Image: imageRes,
      SnapshotId: "s1",
      Elements: Array.Empty<UiaElement>());

    var blocks = PeekuMcpHandlers.BuildContentBlocks(new JsonObject { ["ok"] = true }, see);
    var image = Assert.IsType<ImageContentBlock>(blocks[1]);
    Assert.Equal("AAA", image.Data);
  }

  [Fact]
  public void BuildContentBlocks_WhenNoBase64_DoesNotAddImageBlock()
  {
    var meta = new ResultMeta("t4", DateTimeOffset.UnixEpoch, 1);
    var capture = new CaptureImageResult(
      Ok: true,
      Meta: meta,
      ImagePath: "x.png",
      MimeType: "image/png",
      Width: 1,
      Height: 1,
      Base64Png: null);

    var blocks = PeekuMcpHandlers.BuildContentBlocks(new JsonObject { ["ok"] = true }, capture);
    Assert.Single(blocks);
    Assert.IsType<TextContentBlock>(blocks[0]);
  }
}

