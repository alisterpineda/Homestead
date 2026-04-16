namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.UpdateTextExtractionEngine;

public record UpdateTextExtractionEngineRequest
{
    public required string Name { get; init; }
    public string? Configuration { get; init; }
}
