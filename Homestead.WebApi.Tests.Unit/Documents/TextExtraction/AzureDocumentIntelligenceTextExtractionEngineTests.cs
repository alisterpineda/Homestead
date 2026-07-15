using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class AzureDocumentIntelligenceTextExtractionEngineTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IAzureDocumentIntelligenceRunner _runner = Substitute.For<IAzureDocumentIntelligenceRunner>();
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
        GC.SuppressFinalize(this);
    }

    private static OcrEngineOptions Configured() => new()
    {
        Type = TextExtractionEngineType.AzureDocumentIntelligence,
        Endpoint = "https://x.cognitiveservices.azure.com",
        ApiKey = "k",
        ModelId = AzureModels.PrebuiltLayout
    };

    private AzureDocumentIntelligenceTextExtractionEngine EngineWith(
        OcrEngineOptions options, string key = "azure-layout") =>
        new(key, options, _runner,
            Substitute.For<ILogger<AzureDocumentIntelligenceTextExtractionEngine>>());

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public async Task ExtractTextAsync_UnsupportedContentType_ReturnsNullWithoutCallingAzure(string contentType)
    {
        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, contentType, Ct);

        Assert.Null(result.Text);
        await _runner.DidNotReceive().AnalyzeAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<AzureOutputContentFormat>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    public async Task ExtractTextAsync_SupportedContentType_CallsAzure(string contentType)
    {
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<AzureOutputContentFormat>(), Ct)
            .Returns(new AzureAnalyzeResult("<table><tr><td>x</td></tr></table>", 3));

        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, contentType, Ct);

        Assert.Equal("<table><tr><td>x</td></tr></table>", result.Text);
    }

    // The layout model's Markdown output is the entire reason this engine exists for statements.
    [Fact]
    public async Task ExtractTextAsync_LayoutModel_RequestsMarkdown()
    {
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<AzureOutputContentFormat>(), Ct)
            .Returns(new AzureAnalyzeResult("x", 1));

        await EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        await _runner.Received(1).AnalyzeAsync(
            Arg.Any<Stream>(), AzureModels.PrebuiltLayout, AzureOutputContentFormat.Markdown, Ct);
    }

    [Fact]
    public async Task ExtractTextAsync_ReadModel_RequestsTextAndPassesItsModelId()
    {
        var options = Configured();
        options.ModelId = AzureModels.PrebuiltRead;
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<AzureOutputContentFormat>(), Ct)
            .Returns(new AzureAnalyzeResult("x", 1));

        await EngineWith(options, "azure-read").ExtractTextAsync(_tempFile, "application/pdf", Ct);

        await _runner.Received(1).AnalyzeAsync(
            Arg.Any<Stream>(), AzureModels.PrebuiltRead, AzureOutputContentFormat.Text, Ct);
    }

    [Fact]
    public async Task ExtractTextAsync_Unconfigured_Throws()
    {
        var options = Configured();
        options.Endpoint = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EngineWith(options).ExtractTextAsync(_tempFile, "application/pdf", Ct));

        Assert.Contains("azure-layout", ex.Message);
        Assert.Contains("Endpoint is required.", ex.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_ReportsProvenanceIncludingPageCount()
    {
        _runner.AnalyzeAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<AzureOutputContentFormat>(), Ct)
            .Returns(new AzureAnalyzeResult("x", 12));

        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        var metadata = Assert.IsType<AzureDocumentIntelligenceTextExtractionMetadata>(result.Metadata);
        Assert.Equal("azure-layout", metadata.EngineKey);
        Assert.Equal(AzureModels.PrebuiltLayout, metadata.ModelId);
        Assert.Equal(AzureOutputContentFormat.Markdown, metadata.OutputContentFormat);
        // The durable F0-truncation signal.
        Assert.Equal(12, metadata.PageCount);
    }

    [Fact]
    public void Unconfigured_ReportsUnavailableWithReason()
    {
        var options = Configured();
        options.ApiKey = null;
        var engine = EngineWith(options);

        Assert.False(engine.IsConfigured);
        Assert.Equal("ApiKey is required.", engine.ConfigurationError);
        Assert.Equal(TextExtractionEngineType.AzureDocumentIntelligence, engine.Type);
    }
}
