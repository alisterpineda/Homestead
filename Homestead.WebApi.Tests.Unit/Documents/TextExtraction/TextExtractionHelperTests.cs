using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.TextExtraction;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The extraction-time half of engine resolution: turning a stamped key into an engine, and falling back
// (visibly) when it cannot.
public class TextExtractionHelperTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExtractTextSafeAsync_KnownKey_RunsThatEngineAndReportsNoFallback()
    {
        var tesseract = TestTextExtraction.Engine();
        var azure = TestTextExtraction.Engine("azure-layout");
        azure.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("<table>...</table>", "azure-layout"));
        var helper = TestTextExtraction.Helper(tesseract, azure);

        var result = await helper.ExtractTextSafeAsync("f.pdf", "application/pdf", "azure-layout", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("<table>...</table>", result.Text);
        Assert.Equal("azure-layout", result.Metadata!.EngineKey);
        Assert.Null(result.Metadata.RequestedEngineKey);
        Assert.Null(result.Metadata.FallbackReason);
        await tesseract.DidNotReceive().ExtractTextAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Config changed under an already-enqueued job. Falling back beats failing the job, but the
    // substitution has to be recorded — that is the whole difference between this and running Tesseract
    // silently.
    [Fact]
    public async Task ExtractTextSafeAsync_DanglingKey_FallsBackToBuiltInAndRecordsIt()
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output("plain text"));
        var helper = TestTextExtraction.Helper(tesseract);

        var result = await helper.ExtractTextSafeAsync("f.pdf", "application/pdf", "deleted-engine", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("plain text", result.Text);
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, result.Metadata!.EngineKey);
        Assert.Equal("deleted-engine", result.Metadata.RequestedEngineKey);
        Assert.Contains("not configured", result.Metadata.FallbackReason!);
    }

    [Fact]
    public async Task ExtractTextSafeAsync_UnavailableEngine_FallsBackAndRecordsTheConfigurationError()
    {
        var helper = TestTextExtraction.Helper(
            TestTextExtraction.Engine(), TestTextExtraction.UnavailableEngine());

        var result = await helper.ExtractTextSafeAsync("f.pdf", "application/pdf", "azure-layout", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, result.Metadata!.EngineKey);
        Assert.Equal("azure-layout", result.Metadata.RequestedEngineKey);
        Assert.Equal("Endpoint is required.", result.Metadata.FallbackReason);
    }

    // Nowhere left to fall back to: run it and let the real failure surface, rather than reporting a
    // fallback onto itself.
    [Fact]
    public async Task ExtractTextSafeAsync_BuiltInItselfUnavailable_RunsItAnywayWithNoFallbackRecorded()
    {
        var broken = TestTextExtraction.UnavailableEngine(
            OcrEngineOptions.BuiltInTesseractKey, "TessDataPath is required.");
        broken.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TestTextExtraction.Output(null));
        var helper = TestTextExtraction.Helper(broken);

        var result = await helper.ExtractTextSafeAsync(
            "f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct);

        Assert.True(result.Succeeded);
        Assert.Null(result.Metadata!.RequestedEngineKey);
    }

    [Fact]
    public async Task ExtractTextSafeAsync_EngineThrows_ReturnsFailure()
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TextExtractionOutput>(_ => throw new InvalidOperationException("boom"));
        var helper = TestTextExtraction.Helper(tesseract);

        var result = await helper.ExtractTextSafeAsync(
            "f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct);

        Assert.False(result.Succeeded);
        Assert.Null(result.Metadata);
        Assert.IsType<InvalidOperationException>(result.Error);
    }

    // Cancellation must propagate, not be swallowed as an extraction failure.
    [Fact]
    public async Task ExtractTextSafeAsync_Cancelled_Rethrows()
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TextExtractionOutput>(_ => throw new OperationCanceledException());
        var helper = TestTextExtraction.Helper(tesseract);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            helper.ExtractTextSafeAsync("f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct));
    }

    // Two engines sharing a key is a config bug. It must throw rather than let one silently shadow the
    // other — which is exactly what a First()-based lookup would do.
    [Fact]
    public async Task ExtractTextSafeAsync_DuplicateKeys_Throws()
    {
        var helper = TestTextExtraction.Helper(
            TestTextExtraction.Engine("dupe"), TestTextExtraction.Engine("dupe"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            helper.ExtractTextSafeAsync("f.pdf", "application/pdf", "dupe", Ct));
    }
}
