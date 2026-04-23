using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalance;

public class GetOpeningBalanceHandler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts)
    : IRequestHandler<GetOpeningBalanceQuery, OpeningBalanceResponse?>
{
    public async Task<OpeningBalanceResponse?> Handle(GetOpeningBalanceQuery request, CancellationToken ct)
    {
        var transaction = await db.OpeningBalancesFor(request.AccountId, request.CommodityId)
            .AsNoTracking()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(ct);

        if (transaction is null) return null;

        var commodity = await db.Commodities.FirstOrDefaultAsync(c => c.Id == request.CommodityId, ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);
        return OpeningBalanceMapping.ToResponse(transaction, accountSplit, commodity);
    }
}
