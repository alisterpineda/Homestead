using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class TesseractTextExtractionEngineTests : IDisposable
{
    private readonly ITesseractRunner _runner;
    private readonly IPdfTextRunner _pdfText;
    private readonly TesseractTextExtractionEngine _sut;
    private readonly string _tempFile;

    public TesseractTextExtractionEngineTests()
    {
        _runner = Substitute.For<ITesseractRunner>();
        _pdfText = Substitute.For<IPdfTextRunner>();
        // Stubbed explicitly to the "pdftotext unavailable" answer: left alone, NSubstitute hands back an
        // empty substitute list, which would silently exercise the page-count-mismatch path instead.
        _pdfText.ExtractPagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>?)null);
        _sut = EngineWith(new OcrEngineOptions());
        _tempFile = Path.GetTempFileName();
    }

    private TesseractTextExtractionEngine EngineWith(OcrEngineOptions options, string key = "tesseract") =>
        new(key, options, _runner, _pdfText, Substitute.For<ILogger<TesseractTextExtractionEngine>>());

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public async Task ExtractTextAsync_UnsupportedContentType_ReturnsNullText(string contentType)
    {
        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Null(result.Text);
        await _runner.DidNotReceive().OcrAsync(
            Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/tiff")]
    [InlineData("image/bmp")]
    [InlineData("image/gif")]
    public async Task ExtractTextAsync_SupportedImageType_CallsRunner(string contentType)
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("ocr text");

        var result = await _sut.ExtractTextAsync(_tempFile, contentType, CancellationToken.None);

        Assert.Equal("ocr text", result.Text);
        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.Language == "eng" && o.Dpi == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithConfiguredLanguages_PassesLanguageToRunner()
    {
        // Languages come from the engine's own OcrEngineOptions instance, not a per-call config blob.
        var engine = EngineWith(new OcrEngineOptions { Languages = [TesseractLanguage.Fra, TesseractLanguage.Deu] });
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("texte");

        await engine.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), Arg.Is<TesseractRunOptions>(o => o.Language == "fra+deu"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_ImageWithDefaultOptions_UsesDefaultLanguage()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), Arg.Is<TesseractRunOptions>(o => o.Language == "eng"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_RunnerReturnsNull_ReturnsNullText()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        Assert.Null(result.Text);
    }

    // Each engine instance runs on its own options bag, so the shared, stateless runner has to be told
    // the tessdata path / psm / oem per call — exactly what the old IOptions-capturing runner could not do.
    [Fact]
    public async Task ExtractTextAsync_TwoInstances_EachPassesItsOwnRunOptions()
    {
        var first = EngineWith(new OcrEngineOptions { TessDataPath = "/a", Psm = 6, Oem = 1 }, "first");
        var second = EngineWith(new OcrEngineOptions { TessDataPath = "/b", Psm = 3, Oem = 3 }, "second");
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        await first.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);
        await second.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        await _runner.Received(1).OcrAsync(Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.TessDataPath == "/a" && o.Psm == 6 && o.Oem == 1),
            Arg.Any<CancellationToken>());
        await _runner.Received(1).OcrAsync(Arg.Any<Stream>(),
            Arg.Is<TesseractRunOptions>(o => o.TessDataPath == "/b" && o.Psm == 3 && o.Oem == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_Image_ReportsProvenance()
    {
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("text");

        var result = await _sut.ExtractTextAsync(_tempFile, "image/png", CancellationToken.None);

        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal("tesseract", metadata.EngineKey);
        Assert.Equal("eng", metadata.Language);
        Assert.Equal(0, metadata.PagesFromTextLayer);
        Assert.Equal(1, metadata.PagesOcred);
        Assert.Null(metadata.RequestedEngineKey);
        // No text-layer pass runs for an image.
        Assert.Null(metadata.TextLayerExtractor);
        await _pdfText.DidNotReceive().ExtractPagesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_PdfPopplerReadsEveryPage_UsesPopplerTextAndSkipsOcr()
    {
        File.WriteAllBytes(_tempFile, BuildTextThenImagePdf());
        _pdfText.ExtractPagesAsync(_tempFile, Arg.Any<CancellationToken>())
            .Returns(["\n\n  Col A    Col B  \n", "Second   page\n"]);

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);

        // Poppler's text, not PdfPig's: leading blank lines dropped but the first line's indentation (a
        // column position under -layout) kept; trailing whitespace dropped per page.
        Assert.Equal("  Col A    Col B" + Environment.NewLine + "Second   page", result.Text);
        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal(2, metadata.PagesFromTextLayer);
        Assert.Equal(0, metadata.PagesOcred);
        Assert.Equal(TesseractTextExtractionMetadata.PopplerTextLayer, metadata.TextLayerExtractor);
        await _runner.DidNotReceive().OcrAsync(
            Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_PdfPopplerPageBlank_OcrsJustThatPage()
    {
        File.WriteAllBytes(_tempFile, BuildTextThenImagePdf());
        _pdfText.ExtractPagesAsync(_tempFile, Arg.Any<CancellationToken>())
            .Returns(["poppler text\n", "  \n"]);
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("scanned text");

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);

        Assert.Equal("poppler text" + Environment.NewLine + "scanned text", result.Text);
        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal(1, metadata.PagesFromTextLayer);
        Assert.Equal(1, metadata.PagesOcred);
        Assert.Equal(TesseractTextExtractionMetadata.PopplerTextLayer, metadata.TextLayerExtractor);
        await _runner.Received(1).OcrAsync(
            Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractTextAsync_PdfPopplerUnavailable_FallsBackToPdfPig()
    {
        // _pdfText answers null (pdftotext missing or failed) by default.
        File.WriteAllBytes(_tempFile, BuildTextThenImagePdf());
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("scanned text");

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);

        Assert.Contains(PdfPigPageText, result.Text);
        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal(1, metadata.PagesFromTextLayer);
        Assert.Equal(1, metadata.PagesOcred);
        Assert.Equal(TesseractTextExtractionMetadata.PdfPigTextLayer, metadata.TextLayerExtractor);
    }

    // The runner's contract is null-on-failure, but a throw must not cost the PDF its text either.
    [Fact]
    public async Task ExtractTextAsync_PdfTextRunnerThrows_FallsBackToPdfPig()
    {
        File.WriteAllBytes(_tempFile, BuildTextThenImagePdf());
        _pdfText.ExtractPagesAsync(_tempFile, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>?>(_ => throw new IOException("pipe broke"));
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("scanned text");

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);

        Assert.Contains(PdfPigPageText, result.Text);
        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal(1, metadata.PagesFromTextLayer);
        Assert.Equal(1, metadata.PagesOcred);
        Assert.Equal(TesseractTextExtractionMetadata.PdfPigTextLayer, metadata.TextLayerExtractor);
    }

    // An embedded form feed in the text splits a page in two, so Poppler reports MORE pages than the PDF
    // has; fewer is the defensive case. Either way the per-page text can't be trusted to line up.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ExtractTextAsync_PdfPopplerPageCountMismatch_FallsBackToPdfPig(int popplerPageCount)
    {
        File.WriteAllBytes(_tempFile, BuildTextThenImagePdf());
        _pdfText.ExtractPagesAsync(_tempFile, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Repeat("only one page from poppler", popplerPageCount).ToList());
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns("scanned text");

        var result = await _sut.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);

        Assert.Contains(PdfPigPageText, result.Text);
        Assert.DoesNotContain("only one page from poppler", result.Text);
        var metadata = Assert.IsType<TesseractTextExtractionMetadata>(result.Metadata);
        Assert.Equal(TesseractTextExtractionMetadata.PdfPigTextLayer, metadata.TextLayerExtractor);
    }

    [Fact]
    public async Task ExtractTextAsync_PdfPageWithoutTextLayer_RendersPageToPngForOcr()
    {
        var (result, ocrInput) = await ExtractImageOnlyPdfAsync(_sut);

        Assert.Equal("scanned text", result.Text);
        Assert.NotNull(ocrInput);
        Assert.True(IsPng(ocrInput));
        // The embedded image must survive the render: at the default 300 DPI its centre, PDF point
        // (150, 550) on the 842pt-tall page, lands at pixel (625, 1217).
        using var rendered = SKBitmap.Decode(ocrInput);
        Assert.Equal(SKColors.SteelBlue, rendered.GetPixel(625, 1217));
    }

    [Fact]
    public async Task ExtractTextAsync_PdfPageWithoutTextLayer_RendersAtConfiguredDpi()
    {
        // 150, not the 300 default: PDFtoImage's own default is also 300, so only a non-default value
        // shows the configured DPI is actually honoured.
        var engine = EngineWith(new OcrEngineOptions { Dpi = 150 });

        var (_, ocrInput) = await ExtractImageOnlyPdfAsync(engine);

        // A4 is 595 x 842pt; at 150 DPI that's 1239.6 x 1754.2px.
        using var rendered = SKBitmap.Decode(ocrInput);
        Assert.InRange(rendered.Width, 1239, 1240);
        Assert.InRange(rendered.Height, 1754, 1755);
    }

    // Runs the engine over a one-page image-only PDF and captures the bytes it handed to tesseract.
    private async Task<(TextExtractionOutput Result, byte[]? OcrInput)> ExtractImageOnlyPdfAsync(
        TesseractTextExtractionEngine engine)
    {
        File.WriteAllBytes(_tempFile, BuildImageOnlyPdf());
        byte[]? ocrInput = null;
        _runner.OcrAsync(Arg.Any<Stream>(), Arg.Any<TesseractRunOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var copy = new MemoryStream();
                call.Arg<Stream>().CopyTo(copy);
                ocrInput = copy.ToArray();
                return "scanned text";
            });

        var result = await engine.ExtractTextAsync(_tempFile, "application/pdf", CancellationToken.None);
        return (result, ocrInput);
    }

    // A one-page A4 PDF whose only content is a SteelBlue PNG at PDF (50, 500)–(250, 600) — no text layer,
    // so the engine must rasterize the page (PDFtoImage) and hand the render to tesseract.
    private static byte[] BuildImageOnlyPdf()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(SteelBluePng(), new PdfRectangle(50, 500, 250, 600));
        return builder.Build();
    }

    private const string PdfPigPageText = "Embedded text layer";

    // A two-page A4 PDF: page 1 carries a real text layer, page 2 only the SteelBlue PNG. Built in-test
    // rather than added under tests/fixtures, whose README asks for a written reason per fixture.
    private static byte[] BuildTextThenImagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText(PdfPigPageText, 12, new PdfPoint(50, 700), font);
        builder.AddPage(PageSize.A4).AddPng(SteelBluePng(), new PdfRectangle(50, 500, 250, 600));
        return builder.Build();
    }

    private static byte[] SteelBluePng()
    {
        using var bitmap = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    [Fact]
    public void DefaultOptions_IsConfigured()
    {
        Assert.True(_sut.IsConfigured);
        Assert.Null(_sut.ConfigurationError);
        Assert.Equal(TextExtractionEngineType.Tesseract, _sut.Type);
    }

    [Fact]
    public void NoTessDataPath_ReportsUnavailableWithReason()
    {
        var engine = EngineWith(new OcrEngineOptions { TessDataPath = "" });

        Assert.False(engine.IsConfigured);
        Assert.Contains("TessDataPath", engine.ConfigurationError!);
    }
}
