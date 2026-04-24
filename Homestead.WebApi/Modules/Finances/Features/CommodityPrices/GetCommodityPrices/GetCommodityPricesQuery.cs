using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public record GetCommodityPricesQuery : PaginatedRequest, IRequest<PaginatedResponse<CommodityPriceResponse>>
{
    public Guid? CommodityId { get; init; }
    public Guid? QuoteCurrencyId { get; init; }
    public DateTimeOffset? FromAt { get; init; }
    public DateTimeOffset? ToAt { get; init; }
}
