using System.Collections.Concurrent;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace Homestead.WebApi.Features.Documents.TextExtraction;

public class AzureDocumentIntelligenceRunner(ILogger<AzureDocumentIntelligenceRunner> logger)
    : IAzureDocumentIntelligenceRunner
{
    private readonly ConcurrentDictionary<(string Endpoint, string ApiKey), DocumentIntelligenceClient> _clients = new();

    public async Task<string?> AnalyzeAsync(Stream document, string endpoint, string apiKey, CancellationToken ct)
    {
        logger.LogDebug("Calling Azure Document Intelligence at {Endpoint}", endpoint);

        var client = _clients.GetOrAdd((endpoint, apiKey),
            key => new DocumentIntelligenceClient(new Uri(key.Endpoint), new AzureKeyCredential(key.ApiKey)));

        var binaryData = await BinaryData.FromStreamAsync(document, ct);

        var options = new AnalyzeDocumentOptions("prebuilt-layout", binaryData)
        {
            OutputContentFormat = DocumentContentFormat.Markdown
        };

        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, ct);
        var result = operation.Value;

        return string.IsNullOrWhiteSpace(result.Content) ? null : result.Content.Trim();
    }
}
