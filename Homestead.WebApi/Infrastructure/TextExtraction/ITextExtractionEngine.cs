using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

// What an engine produces: the text, plus provenance describing how it was produced. Text is nullable
// (an unsupported content type, or a document with nothing in it); Metadata never is, because "engine X
// ran and found nothing" is itself worth recording.
public record TextExtractionOutput(string? Text, EngineTextExtractionMetadata Metadata);

// One configured engine instance. Instances are built from config at DI registration and registered as
// IEnumerable<ITextExtractionEngine> — never keyed, because MS DI cannot enumerate keyed registrations
// and the picker needs to enumerate them. Engines are singletons over their own options bag and must
// never touch AppDbContext (mirroring the AI providers): an engine's DEFINITION ("Ocr:Engines:{key}") and
// the CHOICE of default ("Ocr:DefaultEngine") are both deployment facts, never product data.
public interface ITextExtractionEngine
{
    // The config instance name ("tesseract", "azure-layout"). Unique across registrations; the registry
    // looks up with SingleOrDefault so a duplicate throws loudly rather than shadowing.
    string Key { get; }

    TextExtractionEngineType Type { get; }

    bool IsConfigured { get; }

    // Why this engine cannot run, when IsConfigured is false. Surfaced to the picker so an unusable
    // engine reads as unavailable-with-reason instead of failing at extraction time.
    string? ConfigurationError { get; }

    // The configured per-engine job budget (Ocr:Engines:{key}:JobTimeout), or null for the global
    // Processing:JobTimeout. See OcrEngineOptions.JobTimeout.
    TimeSpan? JobTimeout { get; }

    Task<TextExtractionOutput> ExtractTextAsync(string filePath, string contentType, CancellationToken ct);
}
