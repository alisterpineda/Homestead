using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public class GetOpeningBalanceHandler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts)
{
    public async Task<OpeningBalanceResponse?> HandleAsync(Guid accountId, Guid commodityId, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, accountId, ct);

        var transaction = await db.OpeningBalancesFor(accountId, commodityId)
            .AsNoTracking()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(ct);

        if (transaction is null) return null;

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);
        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
