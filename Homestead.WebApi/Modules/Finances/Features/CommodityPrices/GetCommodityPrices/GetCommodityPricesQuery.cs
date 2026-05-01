using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public record GetCommodityPricesQuery : PaginatedRequest, IRequest<PaginatedResponse<CommodityPriceResponse>>
{
    public Guid? CommodityId { get; init; }
    public Guid? QuoteCurrencyId { get; init; }
    public DateTimeOffset? FromAt { get; init; }
    public DateTimeOffset? ToAt { get; init; }

    [FromQuery(Name = "Sources")]
    public CommodityPriceSource[]? Sources { get; init; }

    public CommodityPriceSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
