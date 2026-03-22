using Homestead.WebApi.Domains.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public record CreateTextExtractionEngineRequest : IRequest<TextExtractionEngineResponse>
{
    public required string Name { get; init; }
    public required TextExtractionEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
}
