namespace Homestead.WebApi.TextExtraction;

public interface IAzureDocumentIntelligenceRunner
{
    Task<string?> AnalyzeAsync(Stream document, string endpoint, string apiKey, CancellationToken ct);
}
