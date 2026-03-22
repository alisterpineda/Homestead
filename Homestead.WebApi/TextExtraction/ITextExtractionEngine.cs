namespace Homestead.WebApi.TextExtraction;

public interface ITextExtractionEngine
{
    Task<string?> ExtractTextAsync(string filePath, string contentType, string? configuration, CancellationToken ct);
}
