using System.Text.Json;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// What goes on the wire for an AiRequest: the exact Beta messages request, driven as a pure function (no HTTP).
public class AnthropicRequestMapperTests
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { title = new { type = "string" } },
        required = new[] { "title" },
        additionalProperties = false,
    });

    private static AiOptions Options(bool refusalFallback = true) => new()
    {
        ApiKey = "sk-test", Model = "claude-opus-5-5", RefusalFallback = refusalFallback,
    };

    private static AiRequest Request(
        AiEffort effort = AiEffort.Medium, bool longOutput = false, params AiContentPart[] content) =>
        new(AiFeature.Classification, effort, "You classify documents.",
            content.Length == 0 ? [new AiTextPart("hello")] : content,
            Schema, longOutput, "Test request");

    [Theory]
    [InlineData(AiEffort.Low, "low")]
    [InlineData(AiEffort.Medium, "medium")]
    [InlineData(AiEffort.High, "high")]
    [InlineData(AiEffort.XHigh, "xhigh")]
    [InlineData(AiEffort.Max, "max")]
    public void Effort_IsSentAsItsWireString(AiEffort effort, string wire)
    {
        var p = AnthropicRequestMapper.ToParams(Request(effort), Options());

        Assert.Equal(wire, p.OutputConfig!.Effort!.Raw());
    }

    [Theory]
    [InlineData(AiEffort.Low, false, false)]
    [InlineData(AiEffort.Medium, false, false)]
    [InlineData(AiEffort.High, false, false)]
    [InlineData(AiEffort.XHigh, false, true)]
    [InlineData(AiEffort.Max, false, true)]
    [InlineData(AiEffort.Medium, true, true)]
    public void Streams_ForLongOutputOrTheTopEfforts_WithTheMatchingCap(AiEffort effort, bool longOutput, bool streams)
    {
        var request = Request(effort, longOutput);
        var p = AnthropicRequestMapper.ToParams(request, Options());

        Assert.Equal(streams, AnthropicRequestMapper.ShouldStream(request));
        Assert.Equal(
            streams ? AnthropicRequestMapper.StreamedMaxTokens : AnthropicRequestMapper.BufferedMaxTokens,
            p.MaxTokens);
    }

    [Fact]
    public void ModelSystemAndSchema_AreSentVerbatim()
    {
        var p = AnthropicRequestMapper.ToParams(Request(), Options());

        Assert.Equal("claude-opus-5-5", p.Model.Raw());
        Assert.True(p.System!.TryPickString(out var system));
        Assert.Equal("You classify documents.", system);
        var schema = JsonSerializer.SerializeToElement(p.OutputConfig!.Format!.Schema);
        Assert.True(JsonElement.DeepEquals(Schema, schema), schema.GetRawText());
    }

    // Current Claude models reject sampling parameters and explicit thinking budgets with a 400; citations don't
    // combine with a format.
    [Fact]
    public void NoTemperature_NoThinking()
    {
        var p = AnthropicRequestMapper.ToParams(Request(), Options());

        Assert.DoesNotContain("temperature", p.RawBodyData.Keys);
        Assert.DoesNotContain("top_p", p.RawBodyData.Keys);
        Assert.DoesNotContain("thinking", p.RawBodyData.Keys);
    }

    [Fact]
    public void RefusalFallback_On_SendsDefaultFallbacksAndTheBeta()
    {
        var p = AnthropicRequestMapper.ToParams(Request(), Options(refusalFallback: true));

        Assert.NotNull(p.Fallbacks);
        Assert.True(p.Fallbacks!.TryPickDefault(out _));
        Assert.Equal([AnthropicBeta.ServerSideFallback2026_07_01], p.Betas!.Select(b => b.Value()));
    }

    [Fact]
    public void RefusalFallback_Off_SendsNeither()
    {
        var p = AnthropicRequestMapper.ToParams(Request(), Options(refusalFallback: false));

        // Omitted from the body, not sent as an explicit null.
        Assert.False(p.RawBodyData.ContainsKey("fallbacks"));
        Assert.Null(p.Betas);
    }

    [Fact]
    public void Content_IsOrdered_PdfThenImageThenText()
    {
        var p = AnthropicRequestMapper.ToParams(
            Request(AiEffort.Medium, false,
                new AiTextPart("first text"),
                new AiImagePart([1, 2, 3], "image/png"),
                new AiTextPart("second text"),
                new AiPdfPart([4, 5, 6])),
            Options());

        var message = Assert.Single(p.Messages);
        Assert.Equal("user", message.Role.Raw());
        Assert.True(message.Content.TryPickBetaContentBlockParams(out var blocks));
        Assert.Collection(blocks,
            b =>
            {
                Assert.True(b.TryPickRequestDocumentBlock(out var doc));
                Assert.True(doc.Source.TryPickBetaBase64Pdf(out var pdf));
                Assert.Equal(Convert.ToBase64String([4, 5, 6]), pdf.Data);
            },
            b =>
            {
                Assert.True(b.TryPickImage(out var image));
                Assert.True(image.Source.TryPickBetaBase64Image(out var source));
                Assert.Equal(Convert.ToBase64String([1, 2, 3]), source.Data);
                Assert.Equal("image/png", source.MediaType.Raw());
            },
            b => Assert.Equal("first text", Assert.IsType<BetaTextBlockParam>(b.Value).Text),
            b => Assert.Equal("second text", Assert.IsType<BetaTextBlockParam>(b.Value).Text));
    }

    // Uploaded PDFs are referenced by file ID, in part order, and never carry their bytes.
    [Fact]
    public void PdfFileIds_ReferenceTheUploads_InPartOrder()
    {
        var p = AnthropicRequestMapper.ToParams(
            Request(AiEffort.Medium, false,
                new AiPdfPart([1]), new AiTextPart("text"), new AiPdfPart([2])),
            Options(), ["file_a", "file_b"]);

        Assert.True(Assert.Single(p.Messages).Content.TryPickBetaContentBlockParams(out var blocks));
        Assert.Equal(["file_a", "file_b"], blocks.Take(2).Select(b =>
        {
            Assert.True(b.TryPickRequestDocumentBlock(out var doc));
            Assert.True(doc.Source.TryPickBetaFileDocument(out var file));
            return file.FileID;
        }));
        Assert.DoesNotContain("base64", JsonSerializer.Serialize(p.RawBodyData));
    }

    [Fact]
    public void PdfFileIds_MustMatchThePdfParts() =>
        Assert.Throws<ArgumentException>(() => AnthropicRequestMapper.ToParams(
            Request(AiEffort.Medium, false, new AiPdfPart([1])), Options(), ["file_a", "file_b"]));

    [Fact]
    public void BytePart_ToString_NeverPrintsTheBytes()
    {
        var part = new AiImagePart([1, 2, 3], "image/png");

        Assert.StartsWith("(image/png, 3 bytes, sha256:", part.ToString());
        Assert.StartsWith("(application/pdf, 3 bytes, sha256:", new AiPdfPart([1, 2, 3]).ToString());
    }
}
