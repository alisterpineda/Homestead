using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;

public record GetCommoditiesQuery : PaginatedRequest, IRequest<PaginatedResponse<CommodityResponse>>
{
    public CommodityKind? Kind { get; init; }
    public string? Search { get; init; }
}
