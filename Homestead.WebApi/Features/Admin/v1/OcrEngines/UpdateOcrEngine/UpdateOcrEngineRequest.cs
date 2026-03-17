namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.UpdateOcrEngine;

public record UpdateOcrEngineRequest
{
    public required string Name { get; init; }
    public string? Configuration { get; init; }
}
