namespace Homestead.WebApi.Infrastructure.TextExtraction;

// Runs one extraction with a named engine. The key is required and concrete: callers on the reprocess
// path get theirs stamped into the job payload at enqueue, and callers on the default path (uploads,
// statement parsing) ask ITextExtractionEngineResolver for the household default first. This class stays
// settings-free — it knows how to run an engine, not which one a household prefers.
public class TextExtractionHelper(IEnumerable<ITextExtractionEngine> engines, ILogger<TextExtractionHelper> logger)
{
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

        try
        {
            var output = await engine.ExtractTextAsync(filePath, contentType, ct);

            // The engine describes itself; only this method knows a substitution happened, so it stamps
            // the fallback fields on the way out.
            var metadata = fallbackFrom is null
                ? output.Metadata
                : output.Metadata with { RequestedEngineKey = fallbackFrom, FallbackReason = fallbackReason };

            return TextExtractionResult.Success(output.Text, metadata);
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
