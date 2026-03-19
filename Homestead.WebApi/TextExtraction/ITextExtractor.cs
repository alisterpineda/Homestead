namespace Homestead.WebApi.TextExtraction;

public interface ITextExtractor
{
    bool CanExtract(string contentType);
    Task<string?> ExtractTextAsync(string filePath, TextExtractionContext context, CancellationToken ct);
}
