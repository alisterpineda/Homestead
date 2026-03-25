using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public record TextExtractionContext(
    TextExtractionEngineType EngineType,
    string? Configuration);
