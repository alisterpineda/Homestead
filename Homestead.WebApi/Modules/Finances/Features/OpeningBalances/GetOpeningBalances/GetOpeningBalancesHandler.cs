using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public class GetOpeningBalancesHandler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts)
{
    public async Task<IReadOnlyList<OpeningBalanceResponse>> HandleAsync(
        Guid accountId, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, accountId, ct);

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);

        var transactions = await db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .AsNoTracking()
            .Where(t => t.Splits.Any(s => s.AccountId == accountId))
            .Include(t => t.Splits)
            .ToListAsync(ct);

        return transactions
            .Select(t =>
            {
                var accountSplit = OpeningBalanceMapping.AccountSideSplit(t, openingBalancesAccount.Id);
                return OpeningBalanceMapping.ToResponse(t, accountSplit);
            })
            .OrderBy(r => r.CommodityId)
            .ToList();
    }
}
