namespace HomeSuiteHome.WebApi.TextExtraction;

public interface ITextExtractor
{
    bool CanExtract(string contentType);
    Task<string?> ExtractTextAsync(string filePath, CancellationToken ct);
}
