namespace Homestead.WebApi.TextExtraction;

public class TextExtractionHelper(
    IEnumerable<ITextExtractor> textExtractors,
    ITextExtractionEngineResolver engineResolver)
{
    public async Task<string?> ExtractTextSafeAsync(
        string filePath, string contentType, Guid? engineId,
        ILogger logger, CancellationToken ct)
    {
        var extractor = textExtractors.FirstOrDefault(e => e.CanExtract(contentType));
        if (extractor is null)
            return null;

        try
        {
            var engineContext = await engineResolver.ResolveAsync(engineId, ct);
            return await extractor.ExtractTextAsync(filePath, engineContext, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Text extraction failed for {FilePath}", filePath);
            return null;
        }
    }
}
