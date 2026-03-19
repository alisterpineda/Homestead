using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.TextExtraction;

public record TextExtractionContext(
    TextExtractionEngineType EngineType,
    string? Configuration);
