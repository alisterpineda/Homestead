using System.Linq.Expressions;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities;

internal static class CommodityProjections
{
    public static Expression<Func<Commodity, CommodityResponse>> ToResponse(AppDbContext db) =>
        c => new CommodityResponse
        {
            Id = c.Id,
            Symbol = c.Symbol,
            Exchange = c.Exchange,
            Name = c.Name,
            Kind = c.Kind,
            DisplayPrecision = c.DisplayPrecision,
            IsSystemManaged = c.IsSystemManaged,
            PriceCount = db.CommodityPrices.Count(p => p.CommodityId == c.Id),
            LatestPriceAt = db.CommodityPrices
                .Where(p => p.CommodityId == c.Id)
                .Max(p => (DateTimeOffset?)p.PriceAt),
            CanDelete = !c.IsSystemManaged
                && !db.Accounts.Any(a => a.ReportingCurrencyId == c.Id)
                && !db.AccountTransactionSplits.Any(s => s.CommodityId == c.Id || s.UnitPriceCommodityId == c.Id)
                && !db.CommodityPrices.Any(p => p.CommodityId == c.Id || p.QuoteCurrencyId == c.Id),
        };
}
