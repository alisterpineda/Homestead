using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Features.Documents.TextExtraction;

public record TextExtractionContext(
    TextExtractionEngineType EngineType,
    string? Configuration);
