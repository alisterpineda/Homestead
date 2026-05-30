using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.CreateTextExtractionEngine;

public record CreateTextExtractionEngineCommand : ICommand<TextExtractionEngineResponse>
{
    public required string Name { get; init; }
    public required TextExtractionEngineType EngineType { get; init; }
    public string? Configuration { get; init; }
}
