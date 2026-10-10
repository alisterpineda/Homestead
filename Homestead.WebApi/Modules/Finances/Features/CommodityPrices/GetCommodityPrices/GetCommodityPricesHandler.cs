using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public class GetCommodityPricesHandler(AppDbContext db)
{
    public async Task<PaginatedResponse<CommodityPriceResponse>> Handle(
        GetCommodityPricesQuery request, CancellationToken ct)
    {
        var query = db.CommodityPrices.AsNoTracking();

        if (request.CommodityId is { } commodityId)
            query = query.Where(p => p.CommodityId == commodityId);
        if (request.QuoteCurrencyId is { } quoteId)
            query = query.Where(p => p.QuoteCurrencyId == quoteId);
        if (request.FromAt is { } fromAt)
            query = query.Where(p => p.PriceAt >= fromAt);
        if (request.ToAt is { } toAt)
            query = query.Where(p => p.PriceAt <= toAt);
        if (request.Sources is { Length: > 0 } sources)
            query = query.Where(p => sources.Contains(p.Source));

        return await ApplySort(query, request.SortBy, request.SortDir)
            .Select(p => new CommodityPriceResponse
            {
                Id = p.Id,
                CommodityId = p.CommodityId,
                QuoteCurrencyId = p.QuoteCurrencyId,
                PriceAt = p.PriceAt,
                Rate = p.Rate,
                Source = p.Source,
                CreatedByTransactionId = p.CreatedByTransactionId,
            })
            .ToPaginatedAsync(request, ct);
    }

    private static IOrderedQueryable<CommodityPrice> ApplySort(
        IQueryable<CommodityPrice> query, CommodityPriceSortBy? sortBy, SortDirection? sortDir)
    {
        var effectiveSortBy = sortBy ?? CommodityPriceSortBy.PriceAt;
        var effectiveSortDir = sortDir ?? SortDirection.Desc;

        return (effectiveSortBy, effectiveSortDir) switch
        {
            (CommodityPriceSortBy.PriceAt, SortDirection.Desc) => query.OrderByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.PriceAt, SortDirection.Asc) => query.OrderBy(p => p.PriceAt),
            (CommodityPriceSortBy.Rate, SortDirection.Desc) => query.OrderByDescending(p => p.Rate).ThenByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.Rate, SortDirection.Asc) => query.OrderBy(p => p.Rate).ThenByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.Source, SortDirection.Desc) => query.OrderByDescending(p => p.Source).ThenByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.Source, SortDirection.Asc) => query.OrderBy(p => p.Source).ThenByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.Quote, SortDirection.Desc) => query.OrderByDescending(p => p.QuoteCurrencyId).ThenByDescending(p => p.PriceAt),
            (CommodityPriceSortBy.Quote, SortDirection.Asc) => query.OrderBy(p => p.QuoteCurrencyId).ThenByDescending(p => p.PriceAt),
            _ => query.OrderByDescending(p => p.PriceAt),
        };
    }
}
