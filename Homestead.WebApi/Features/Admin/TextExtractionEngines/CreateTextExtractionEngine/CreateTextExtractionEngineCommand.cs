using Homestead.WebApi.Domain.Settings;
using MediatR;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.CreateTextExtractionEngine;

public record CreateTextExtractionEngineCommand : IRequest<TextExtractionEngineResponse>
{
    public required string Name { get; init; }
    public required TextExtractionEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
}
