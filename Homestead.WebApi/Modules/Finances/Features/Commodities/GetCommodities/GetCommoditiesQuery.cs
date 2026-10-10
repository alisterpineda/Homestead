using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.GetCommodities;

public record GetCommoditiesQuery : PaginatedRequest
{
    public CommodityKind? Kind { get; init; }
    public string? Search { get; init; }
}
