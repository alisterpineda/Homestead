using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public class GetOpeningBalanceHandler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts)
{
    public async Task<OpeningBalanceResponse?> Handle(GetOpeningBalanceQuery request, CancellationToken ct)
    {
        await AccountVisibility.EnsureUserVisibleAsync(db, request.AccountId, ct);

        var transaction = await db.OpeningBalancesFor(request.AccountId, request.CommodityId)
            .AsNoTracking()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(ct);

        if (transaction is null) return null;

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);
        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
