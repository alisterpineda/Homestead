using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Services;

internal static class OpeningBalanceQueries
{
    public static IQueryable<OpeningBalanceTransaction> OpeningBalancesFor(
        this AppDbContext db, Guid accountId, Guid commodityId) =>
        db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .Where(t => t.Splits.Any(s => s.AccountId == accountId && s.CommodityId == commodityId));
}
