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
        if (request.FromAt is { } fromAt)
            query = query.Where(p => p.PriceAt >= fromAt);
        if (request.ToAt is { } toAt)
            query = query.Where(p => p.PriceAt <= toAt);

        return await query
            .OrderByDescending(p => p.PriceAt)
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
}
