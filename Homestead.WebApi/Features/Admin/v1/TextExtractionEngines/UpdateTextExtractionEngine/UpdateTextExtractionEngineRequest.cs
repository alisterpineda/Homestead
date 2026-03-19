namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.UpdateTextExtractionEngine;

public record UpdateTextExtractionEngineRequest
{
    public required string Name { get; init; }
    public string? Configuration { get; init; }
}
