using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.GetTextExtractionEngines;

public record GetTextExtractionEnginesQuery : PaginatedRequest, IRequest<PaginatedResponse<TextExtractionEngineResponse>>
{
    public string? Search { get; init; }
}
