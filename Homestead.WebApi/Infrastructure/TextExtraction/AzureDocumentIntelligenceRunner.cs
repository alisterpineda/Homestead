using Homestead.WebApi.Domain.Settings;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// One runner per configured Azure engine instance, holding that instance's credentials. The client is
// built once and reused (mirroring AnthropicAiProvider's client): DocumentIntelligenceClient is a stateless,
// thread-safe HTTP wrapper, so rebuilding per call would mean a fresh TLS handshake per extraction under
// bulk reprocess. The benign init race just discards a redundant, never-used build.
//
// This replaces the old ConcurrentDictionary<(endpoint, apiKey), client> cache: credentials are now fixed
// per instance at construction, so there is nothing left to key a cache on.
public class AzureDocumentIntelligenceRunner(
    string endpoint,
    string apiKey,
    ILogger<AzureDocumentIntelligenceRunner> logger)
    : IAzureDocumentIntelligenceRunner
{
    private DocumentIntelligenceClient? _client;

    public async Task<AzureAnalyzeResult> AnalyzeAsync(
        Stream document, string modelId, AzureOutputContentFormat outputContentFormat, CancellationToken ct)
    {
        logger.LogDebug("Calling Azure Document Intelligence at {Endpoint} with model {ModelId}",
            endpoint, modelId);

        var client = _client ??= new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        var binaryData = await BinaryData.FromStreamAsync(document, ct);

        var options = new AnalyzeDocumentOptions(modelId, binaryData)
        {
            OutputContentFormat = outputContentFormat == AzureOutputContentFormat.Markdown
                ? DocumentContentFormat.Markdown
                : DocumentContentFormat.Text
        };

        // WaitUntil.Completed polls the long-running operation to completion. Note this is bounded by the
        // job processor's 5-minute JobTimeout, not by Azure: a large scan against a busy endpoint can
        // exceed it and surface as a TimeoutException.
        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, ct);
        var result = operation.Value;

        return new AzureAnalyzeResult(
            string.IsNullOrWhiteSpace(result.Content) ? null : result.Content.Trim(),
            result.Pages?.Count ?? 0);
    }
}
