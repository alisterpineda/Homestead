using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

public class GetCommodityPricesHandler(AppDbContext db)
    : IRequestHandler<GetCommodityPricesQuery, PaginatedResponse<CommodityPriceResponse>>
{
    public async Task<PaginatedResponse<CommodityPriceResponse>> Handle(
        GetCommodityPricesQuery request, CancellationToken ct)
    {
        var query = db.CommodityPrices.AsNoTracking();

        if (request.CommodityId is { } commodityId)
            query = query.Where(p => p.CommodityId == commodityId);
        if (request.QuoteCurrencyId is { } quoteId)
            query = query.Where(p => p.QuoteCurrencyId == quoteId);
        if (request.FromDate is { } from)
            query = query.Where(p => p.PriceDate >= from);
        if (request.ToDate is { } to)
            query = query.Where(p => p.PriceDate <= to);

        return await query
            .OrderByDescending(p => p.PriceDate)
            .Select(p => new CommodityPriceResponse
            {
                Id = p.Id,
                CommodityId = p.CommodityId,
                QuoteCurrencyId = p.QuoteCurrencyId,
                PriceDate = p.PriceDate,
                Rate = p.Rate,
            })
            .ToPaginatedAsync(request, ct);
    }
}
