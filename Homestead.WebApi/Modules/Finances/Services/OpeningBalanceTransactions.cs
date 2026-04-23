using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Services;

public static class OpeningBalanceTransactions
{
    public const string Description = "Opening balance";

    public static IQueryable<AccountTransaction> OpeningBalancesFor(
        this AppDbContext db, Guid accountId, Guid commodityId)
    {
        return db.AccountTransactions
            .Where(t => t.Description == Description)
            .Where(t => t.Splits.Any(s => s.AccountId == accountId && s.CommodityId == commodityId));
    }
}
