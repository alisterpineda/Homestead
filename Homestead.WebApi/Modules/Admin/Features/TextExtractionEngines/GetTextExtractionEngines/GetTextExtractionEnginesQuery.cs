using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.GetTextExtractionEngines;

public record GetTextExtractionEnginesQuery : PaginatedRequest, IRequest<PaginatedResponse<TextExtractionEngineResponse>>
{
    public string? Search { get; init; }
}
