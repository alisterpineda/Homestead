using Homestead.WebApi.Processing;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Runs one extraction with a named engine. The key is required and concrete: callers on the reprocess
// path get theirs stamped into the job payload at enqueue, and callers on the default path (uploads) ask
// ITextExtractionEngineResolver for the configured default (Ocr:DefaultEngine) first. This is the ONE
// place a dangling default is substituted — the resolver returns it verbatim so the fallback can be
// recorded in provenance here rather than vanishing silently.
//
// It also owns the extraction DEADLINE. The OCR job handlers declare the engine's budget to the job
// runner (JobTimeoutFor → IJobHandler.ResolveTimeout); this class stops the engine a margin short of that
// same budget and reports a TimeoutException failure. So a slow engine degrades the way any extraction
// failure does — an upload still creates its document without text, a reprocess fails as
// text_extraction_failed — rather than the job dying as timed out with nothing to show for it.
public class TextExtractionHelper(
    IEnumerable<ITextExtractionEngine> engines,
    IOptions<JobProcessingOptions> processing,
    ILogger<TextExtractionHelper> logger)
{
    // What is left of the job budget once extraction is cut off: room for the rest of the handler (hash,
    // duplicate check, thumbnail, the persist transaction). Never more than half the budget, so a short
    // one still leaves extraction the larger share.
    internal static readonly TimeSpan DeadlineMargin = TimeSpan.FromSeconds(30);

    // The job budget for a run of this engine key, or null for the global Processing:JobTimeout. Resolves
    // the key exactly as ExtractTextSafeAsync will, so a dangling or unavailable key reports the budget of
    // the fallback engine — the one that will actually run.
    public TimeSpan? JobTimeoutFor(string engineKey) =>
        OcrEngineOptions.UsableJobTimeout(ResolveEngine(engineKey, out _, out _).JobTimeout);

    // How long extraction may run inside a job of this budget.
    internal static TimeSpan ExtractionDeadline(TimeSpan budget)
    {
        var margin = DeadlineMargin < budget / 2 ? DeadlineMargin : budget / 2;
        return budget - margin;
    }

    public async Task<TextExtractionResult> ExtractTextSafeAsync(
        string filePath, string contentType, string engineKey, CancellationToken ct)
    {
        var engine = ResolveEngine(engineKey, out var fallbackFrom, out var fallbackReason);

        if (fallbackFrom is not null)
        {
            logger.LogWarning(
                "Text extraction engine '{RequestedEngineKey}' is unavailable ({FallbackReason}); "
                + "falling back to '{EngineKey}' for {FilePath}",
                fallbackFrom, fallbackReason, engine.Key, filePath);
        }

        var budget = OcrEngineOptions.UsableJobTimeout(engine.JobTimeout) ?? processing.Value.ResolveJobTimeout();
        var deadline = ExtractionDeadline(budget);

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(deadline);

        Task<TextExtractionOutput>? extraction = null;
        try
        {
            // Started on the thread pool and awaited with WaitAsync, not just handed the token: not every
            // step honours cancellation (PdfPig parsing and PDF rasterization are synchronous, and the
            // Tesseract engine opens the PDF before its first await), and the deadline has to hold even
            // when the engine does not. Called inline, a synchronous prefix would run before WaitAsync
            // was ever attached.
            extraction = Task.Run(() => engine.ExtractTextAsync(filePath, contentType, deadlineCts.Token));
            var output = await extraction.WaitAsync(deadlineCts.Token);

            // The engine describes itself; only this method knows a substitution happened, so it stamps
            // the fallback fields on the way out.
            var metadata = fallbackFrom is null
                ? output.Metadata
                : output.Metadata with { RequestedEngineKey = fallbackFrom, FallbackReason = fallbackReason };

            return TextExtractionResult.Success(output.Text, metadata);
        }
        catch (OperationCanceledException) when (deadlineCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our own deadline, not the job's token: a failed extraction, not a cancelled job.
            if (extraction is { IsCompleted: false })
            {
                logger.LogWarning(
                    "Text extraction engine '{EngineKey}' did not stop when cancelled at its deadline; "
                    + "abandoning it for {FilePath}",
                    engine.Key, filePath);

                // Observe its eventual fault so it is not reported as an unobserved task exception.
                _ = extraction.ContinueWith(
                    static t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return TextExtractionResult.Failure(new TimeoutException(
                $"Text extraction engine '{engine.Key}' did not finish within {deadline}."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TextExtractionResult.Failure(ex);
        }
    }

    // Resolution is by key with SingleOrDefault, not First: two engines sharing a key is a config bug
    // that should throw loudly rather than let one silently shadow the other.
    //
    // A key that no longer resolves means config changed under a job that was already enqueued (the
    // enqueue path rejects an unusable explicit pick with a 400 while a user is watching). Rather than
    // fail the job, fall back to the built-in Tesseract engine and report why — the caller records both
    // in provenance metadata, so the substitution is durable rather than silent.
    private ITextExtractionEngine ResolveEngine(
        string engineKey, out string? fallbackFrom, out string? fallbackReason)
    {
        fallbackFrom = null;
        fallbackReason = null;

        var engine = FindEngine(engineKey);
        var reason = engine switch
        {
            null => $"engine '{engineKey}' is not configured",
            { IsConfigured: false } => engine.ConfigurationError,
            _ => null
        };

        if (reason is null)
            return engine!;

        var builtIn = FindEngine(OcrEngineOptions.BuiltInTesseractKey)
            ?? throw new InvalidOperationException(
                $"The built-in '{OcrEngineOptions.BuiltInTesseractKey}' engine is not registered.");

        // The built-in engine failing its own validation has nowhere left to fall back to; run it and let
        // the real failure surface rather than reporting a fallback onto itself.
        if (ReferenceEquals(builtIn, engine))
            return engine;

        fallbackFrom = engineKey;
        fallbackReason = reason;
        return builtIn;
    }

    private ITextExtractionEngine? FindEngine(string key) =>
        engines.SingleOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
