using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public record CreateTextExtractionEngineRequest
{
    public required string Name { get; init; }
    public required TextExtractionEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
}
