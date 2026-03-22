namespace Homestead.WebApi.Features.Documents.TextExtraction;

public interface IAzureDocumentIntelligenceRunner
{
    Task<string?> AnalyzeAsync(Stream document, string endpoint, string apiKey, CancellationToken ct);
}
