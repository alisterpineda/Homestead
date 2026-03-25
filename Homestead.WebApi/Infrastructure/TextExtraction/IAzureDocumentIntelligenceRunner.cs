namespace Homestead.WebApi.Infrastructure.TextExtraction;

public interface IAzureDocumentIntelligenceRunner
{
    Task<string?> AnalyzeAsync(Stream document, string endpoint, string apiKey, CancellationToken ct);
}
