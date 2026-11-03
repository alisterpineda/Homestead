using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Processing;
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

    // --- The extraction deadline (per-engine job budget) ---

    [Fact]
    public void JobTimeoutFor_EngineWithABudget_ReturnsIt()
    {
        var docling = TestTextExtraction.Engine("docling");
        docling.JobTimeout.Returns(TimeSpan.FromMinutes(20));
        var helper = TestTextExtraction.Helper(TestTextExtraction.Engine(), docling);

        Assert.Equal(TimeSpan.FromMinutes(20), helper.JobTimeoutFor("docling"));
    }

    [Fact]
    public void JobTimeoutFor_EngineWithoutABudget_ReturnsNull()
    {
        var helper = TestTextExtraction.Helper(TestTextExtraction.Engine());

        Assert.Null(helper.JobTimeoutFor(OcrEngineOptions.BuiltInTesseractKey));
    }

    // The job must run under the budget of the engine that will actually run, and a dangling key runs
    // the built-in engine.
    [Fact]
    public void JobTimeoutFor_DanglingKey_ReturnsTheBuiltInEnginesBudget()
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.JobTimeout.Returns(TimeSpan.FromMinutes(7));
        var helper = TestTextExtraction.Helper(tesseract);

        Assert.Equal(TimeSpan.FromMinutes(7), helper.JobTimeoutFor("deleted-engine"));
    }

    [Fact]
    public void JobTimeoutFor_UnavailableEngine_ReturnsTheBuiltInEnginesBudget()
    {
        var unavailable = TestTextExtraction.UnavailableEngine();
        unavailable.JobTimeout.Returns(TimeSpan.FromMinutes(20));
        var helper = TestTextExtraction.Helper(TestTextExtraction.Engine(), unavailable);

        Assert.Null(helper.JobTimeoutFor("azure-layout"));
    }

    // The built-in engine runs even when its own validation fails, so its invalid budget must not reach
    // the job runner as a zero, negative or out-of-range value.
    [Theory]
    [MemberData(nameof(UnusableBudgets))]
    public void JobTimeoutFor_UnusableBudget_ReturnsNull(TimeSpan budget)
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.JobTimeout.Returns(budget);
        var helper = TestTextExtraction.Helper(tesseract);

        Assert.Null(helper.JobTimeoutFor(OcrEngineOptions.BuiltInTesseractKey));
    }

    // ...nor reach the extraction deadline: zero would cancel every extraction at once, a negative or
    // 600-day value would make CancelAfter throw. The global budget applies instead.
    [Theory]
    [MemberData(nameof(UnusableBudgets))]
    public async Task ExtractTextSafeAsync_UnusableBudget_RunsUnderTheGlobalOne(TimeSpan budget)
    {
        var tesseract = TestTextExtraction.Engine();
        tesseract.JobTimeout.Returns(budget);
        tesseract.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(50, call.Arg<CancellationToken>());
                return TestTextExtraction.Output("text");
            });
        var helper = TestTextExtraction.Helper(tesseract);

        var result = await helper.ExtractTextSafeAsync(
            "f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct);

        Assert.True(result.Succeeded, result.Error?.ToString());
        Assert.Equal("text", result.Text);
    }

    public static TheoryData<TimeSpan> UnusableBudgets() =>
        [TimeSpan.Zero, TimeSpan.FromMinutes(-1), TimeSpan.FromDays(600)];

    [Theory]
    [InlineData(300, 270)]   // the 5-minute default: 30 s margin
    [InlineData(1200, 1170)] // a 20-minute engine budget
    [InlineData(40, 20)]     // short budget: margin floored at half
    [InlineData(1, 0.5)]
    public void ExtractionDeadline_LeavesTheMarginCappedAtHalf(double budgetSeconds, double expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            TextExtractionHelper.ExtractionDeadline(TimeSpan.FromSeconds(budgetSeconds)));
    }

    // The point of the deadline: an engine that is too slow is an extraction FAILURE (the upload still
    // creates its document; a reprocess fails as text_extraction_failed), not a cancelled job.
    [Fact]
    public async Task ExtractTextSafeAsync_EngineHonoursTheDeadline_ReturnsTimeoutFailure()
    {
        var slow = TestTextExtraction.Engine();
        slow.JobTimeout.Returns(TimeSpan.FromMilliseconds(200));
        slow.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return TestTextExtraction.Output("never");
            });
        var helper = TestTextExtraction.Helper(slow);

        var result = await helper.ExtractTextSafeAsync(
            "f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct);

        Assert.False(result.Succeeded);
        var timeout = Assert.IsType<TimeoutException>(result.Error);
        Assert.Contains("'tesseract'", timeout.Message);
        Assert.Contains("00:00:00.1", timeout.Message);
    }

    // Not every step honours a token (PdfPig and PDF rasterization are synchronous); the deadline must hold
    // regardless, so the job keeps the margin the rest of its handler needs.
    [Fact]
    public async Task ExtractTextSafeAsync_EngineIgnoresTheToken_StillReturnsTimeoutFailureAtTheDeadline()
    {
        var stuck = TestTextExtraction.Engine();
        stuck.JobTimeout.Returns(TimeSpan.FromMilliseconds(200));
        var never = new TaskCompletionSource<TextExtractionOutput>();
        stuck.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(never.Task);
        var helper = TestTextExtraction.Helper(stuck);

        var result = await helper
            .ExtractTextSafeAsync("f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct)
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(result.Succeeded);
        Assert.IsType<TimeoutException>(result.Error);
    }

    // The Tesseract engine opens the PDF with PdfPig before its first await. A synchronous prefix like that
    // must not run on the caller's thread, where it would finish before the deadline could be attached.
    [Fact]
    public async Task ExtractTextSafeAsync_EngineBlocksBeforeReturningATask_StillReturnsTimeoutFailureAtTheDeadline()
    {
        using var release = new ManualResetEventSlim();
        var blocking = TestTextExtraction.Engine();
        blocking.JobTimeout.Returns(TimeSpan.FromMilliseconds(200));
        blocking.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return Task.FromResult(TestTextExtraction.Output("late"));
            });
        var helper = TestTextExtraction.Helper(blocking);

        try
        {
            var result = await helper
                .ExtractTextSafeAsync("f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct)
                .WaitAsync(TimeSpan.FromSeconds(10), Ct);

            Assert.IsType<TimeoutException>(result.Error);
        }
        finally
        {
            release.Set();
        }
    }

    // An engine without a budget of its own runs under the global Processing:JobTimeout, less the margin.
    [Fact]
    public async Task ExtractTextSafeAsync_EngineWithoutABudget_UsesTheGlobalJobTimeout()
    {
        var slow = TestTextExtraction.Engine();
        slow.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return TestTextExtraction.Output("never");
            });
        var helper = TestTextExtraction.Helper(
            new JobProcessingOptions { JobTimeout = TimeSpan.FromMilliseconds(200) }, slow);

        var result = await helper
            .ExtractTextSafeAsync("f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, Ct)
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.IsType<TimeoutException>(result.Error);
    }

    // The job's own token (shutdown, the job timeout, a lost lease) is the runner's business, never an
    // extraction failure: it must propagate even though the deadline token is linked to it.
    [Fact]
    public async Task ExtractTextSafeAsync_OuterTokenCancelled_Rethrows()
    {
        using var outer = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var engine = TestTextExtraction.Engine();
        engine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await outer.CancelAsync();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return TestTextExtraction.Output("never");
            });
        var helper = TestTextExtraction.Helper(engine);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            helper.ExtractTextSafeAsync(
                "f.pdf", "application/pdf", OcrEngineOptions.BuiltInTesseractKey, outer.Token));
    }
}
