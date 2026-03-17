using Homestead.WebApi.Domains.Settings;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.CreateOcrEngine;

public record CreateOcrEngineRequest
{
    public required string Name { get; init; }
    public required OcrEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
}
