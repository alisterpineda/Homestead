using Homestead.WebApi.Infrastructure.TextExtraction.Configurations;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public class AzureDocumentIntelligenceTextExtractionEngine(IAzureDocumentIntelligenceRunner runner)
    : ITextExtractionEngine
{
    private static readonly HashSet<string> SupportedContentTypes =
    [
        "image/png",
        "image/jpeg",
        "image/tiff",
        "image/bmp",
        "image/gif",
        "application/pdf"
    ];

    public async Task<string?> ExtractTextAsync(string filePath, string contentType, string? configuration, CancellationToken ct)
    {
        if (!SupportedContentTypes.Contains(contentType))
            return null;

        if (!AzureDocumentIntelligenceEngineConfig.TryParse(configuration, out var config)
            || string.IsNullOrWhiteSpace(config!.Endpoint)
            || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence engine is not configured. Endpoint and ApiKey are required.");
        }

        await using var stream = File.OpenRead(filePath);
        return await runner.AnalyzeAsync(stream, config.Endpoint, config.ApiKey, ct);
    }
}
