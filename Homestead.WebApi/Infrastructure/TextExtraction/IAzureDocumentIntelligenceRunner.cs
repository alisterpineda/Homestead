using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// PageCount is what Azure actually analyzed, which is not always what you sent: an F0 (free) resource
// silently analyzes only the first 2 pages of a document and reports success. The engine compares this
// against the source page count and logs the shortfall, and it is persisted in provenance metadata so a
// truncated 30-page statement is diagnosable after the fact rather than a mystery.
public record AzureAnalyzeResult(string? Content, int PageCount);

public interface IAzureDocumentIntelligenceRunner
{
    Task<AzureAnalyzeResult> AnalyzeAsync(
        Stream document, string modelId, AzureOutputContentFormat outputContentFormat, CancellationToken ct);
}
