using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class DoclingTextExtractionEngineTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IDoclingRunner _runner = Substitute.For<IDoclingRunner>();
    private readonly string _tempFile = Path.GetTempFileName();

    public DoclingTextExtractionEngineTests() =>
        Returns(new DoclingConvertResult("| a | b |\n|---|---|\n| 1 | 2 |\n", "success", [], 4.2, "1.38.2"));

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Returns(DoclingConvertResult result) =>
        _runner.ConvertAsync(Arg.Any<Stream>(), Arg.Any<DoclingConvertRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private static OcrEngineOptions Configured() => new()
    {
        Type = TextExtractionEngineType.Docling,
        Endpoint = "http://docling:5001"
    };

    private DoclingTextExtractionEngine EngineWith(
        OcrEngineOptions options, string key = "docling", TimeSpan? globalJobTimeout = null) =>
        new(key, options,
            Options.Create(new JobProcessingOptions { JobTimeout = globalJobTimeout ?? TimeSpan.FromMinutes(5) }),
            _runner, Substitute.For<ILogger<DoclingTextExtractionEngine>>());

    private DoclingConvertRequest SentRequest() =>
        (DoclingConvertRequest)_runner.ReceivedCalls().Single().GetArguments()[1]!;

    [Fact]
    public async Task ExtractTextAsync_Unconfigured_ThrowsBeforeTheRunner()
    {
        var options = Configured();
        options.Endpoint = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EngineWith(options).ExtractTextAsync(_tempFile, "text/plain", Ct));

        Assert.Contains("docling", ex.Message);
        Assert.Contains("Endpoint is required.", ex.Message);
        Assert.Empty(_runner.ReceivedCalls());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public async Task ExtractTextAsync_UnsupportedContentType_ReturnsNullWithoutCallingTheSidecar(string contentType)
    {
        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, contentType, Ct);

        Assert.Null(result.Text);
        Assert.Empty(_runner.ReceivedCalls());
        var metadata = Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata);
        Assert.Equal(DoclingTextExtractionMetadata.UnsupportedContentTypeStatus, metadata.ConversionStatus);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    public async Task ExtractTextAsync_SupportedContentType_ReturnsTheTrimmedMarkdown(string contentType)
    {
        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, contentType, Ct);

        Assert.Equal("| a | b |\n|---|---|\n| 1 | 2 |", result.Text);
        Assert.Equal(contentType, SentRequest().ContentType);
    }

    // The storage file name (hash-based) goes to the sidecar, never anything from the document's title.
    [Fact]
    public async Task ExtractTextAsync_SendsTheStorageFileNameAndConfiguredOptions()
    {
        var options = Configured();
        options.TableMode = "FAST";
        options.ForceOcr = true;

        var result = await EngineWith(options).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        var request = SentRequest();
        Assert.Equal(Path.GetFileName(_tempFile), request.FileName);
        // Validation accepts either case; the sidecar gets its own spelling, and provenance records it.
        Assert.Equal("fast", request.TableMode);
        Assert.Equal("fast", Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata).TableMode);
        Assert.True(request.ForceOcr);
    }

    [Fact]
    public async Task ExtractTextAsync_DefaultOptions_SendNoTableModeNoForceOcr()
    {
        await EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        var request = SentRequest();
        Assert.Null(request.TableMode);
        Assert.False(request.ForceOcr);
    }

    // No JobTimeout of its own: the sidecar still gets a limit, from the global budget the helper uses.
    [Fact]
    public async Task ExtractTextAsync_NoJobTimeout_SendsTheGlobalBudgetLessTheHelpersMargin()
    {
        await EngineWith(Configured(), globalJobTimeout: TimeSpan.FromMinutes(10))
            .ExtractTextAsync(_tempFile, "application/pdf", Ct);

        Assert.Equal(TimeSpan.FromMinutes(10) - TextExtractionHelper.DeadlineMargin, SentRequest().DocumentTimeout);
    }

    // A budget too short for the full margin gives up only half of it — never a zero or negative limit.
    [Fact]
    public async Task ExtractTextAsync_ShortJobTimeout_SendsHalfTheBudget()
    {
        var options = Configured();
        options.JobTimeout = TimeSpan.FromSeconds(20);

        await EngineWith(options).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        Assert.Equal(TimeSpan.FromSeconds(10), SentRequest().DocumentTimeout);
    }

    // The sidecar is told to stop where TextExtractionHelper will stop waiting for it.
    [Fact]
    public async Task ExtractTextAsync_JobTimeoutSet_SendsItLessTheHelpersMargin()
    {
        var options = Configured();
        options.JobTimeout = TimeSpan.FromMinutes(20);
        var engine = EngineWith(options, globalJobTimeout: TimeSpan.FromMinutes(5));

        await engine.ExtractTextAsync(_tempFile, "application/pdf", Ct);

        Assert.Equal(TimeSpan.FromMinutes(20), engine.JobTimeout);
        Assert.Equal(TimeSpan.FromMinutes(20) - TextExtractionHelper.DeadlineMargin, SentRequest().DocumentTimeout);
    }

    [Fact]
    public async Task ExtractTextAsync_ReportsProvenance()
    {
        File.WriteAllBytes(_tempFile, BuildTwoPagePdf());
        var options = Configured();
        options.TableMode = "accurate";

        var result = await EngineWith(options).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        var metadata = Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata);
        Assert.Equal("docling", metadata.EngineKey);
        Assert.Equal("1.38.2", metadata.ServerVersion);
        Assert.Equal("accurate", metadata.TableMode);
        Assert.False(metadata.ForceOcr);
        Assert.Equal("success", metadata.ConversionStatus);
        Assert.Equal(4.2, metadata.ProcessingSeconds);
        // Counted locally: the Markdown response carries no page count.
        Assert.Equal(2, metadata.PageCount);
    }

    [Fact]
    public async Task ExtractTextAsync_Image_CountsOnePage()
    {
        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, "image/png", Ct);

        Assert.Equal(1, Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata).PageCount);
    }

    // An unreadable PDF loses only the page count, never the extraction.
    [Fact]
    public async Task ExtractTextAsync_UnreadablePdf_KeepsTheTextWithZeroPages()
    {
        File.WriteAllText(_tempFile, "not a pdf");

        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        Assert.NotNull(result.Text);
        Assert.Equal(0, Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata).PageCount);
    }

    // Some pages or tables failed: the text is kept, and the status makes the gap durable.
    [Fact]
    public async Task ExtractTextAsync_PartialSuccess_KeepsTheTextAndRecordsTheStatus()
    {
        Returns(new DoclingConvertResult("# Page 1", "partial_success", ["Page 2 timed out"], 30, null));

        var result = await EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct);

        Assert.Equal("# Page 1", result.Text);
        Assert.Equal("partial_success",
            Assert.IsType<DoclingTextExtractionMetadata>(result.Metadata).ConversionStatus);
    }

    [Theory]
    [InlineData("skipped", "# text")]
    [InlineData("failure", "# text")]
    [InlineData("unknown", "# text")]
    [InlineData("success", null)]
    [InlineData("success", "   \n ")]
    [InlineData("partial_success", "")]
    public async Task ExtractTextAsync_NotConvertedOrBlank_ThrowsConversionException(string status, string? markdown)
    {
        Returns(new DoclingConvertResult(markdown, status, ["why"], null, null));

        var ex = await Assert.ThrowsAsync<DoclingConversionException>(() =>
            EngineWith(Configured()).ExtractTextAsync(_tempFile, "application/pdf", Ct));

        Assert.Equal(status, ex.Status);
        Assert.Equal(["why"], ex.Errors);
    }

    [Fact]
    public void Type_IsDocling_AndUnconfiguredReportsTheReason()
    {
        var options = Configured();
        options.TableMode = "slow";
        var engine = EngineWith(options);

        Assert.Equal(TextExtractionEngineType.Docling, engine.Type);
        Assert.False(engine.IsConfigured);
        Assert.Equal("TableMode must be 'fast' or 'accurate'.", engine.ConfigurationError);
    }

    private static byte[] BuildTwoPagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Page one", 12, new PdfPoint(50, 700), font);
        builder.AddPage(PageSize.A4).AddText("Page two", 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }
}
