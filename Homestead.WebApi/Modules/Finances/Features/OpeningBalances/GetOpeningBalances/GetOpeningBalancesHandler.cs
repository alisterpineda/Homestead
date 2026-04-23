using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.GetOpeningBalances;

public class GetOpeningBalancesHandler(
    AppDbContext db,
    ISystemAccountProvider systemAccounts)
    : IRequestHandler<GetOpeningBalancesQuery, IReadOnlyList<OpeningBalanceResponse>>
{
    public async Task<IReadOnlyList<OpeningBalanceResponse>> Handle(
        GetOpeningBalancesQuery request, CancellationToken ct)
    {
        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);

        var transactions = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.Description == OpeningBalanceTransactions.Description)
            .Where(t => t.Splits.Any(s => s.AccountId == request.AccountId))
            .Include(t => t.Splits)
            .ToListAsync(ct);

        var commodityIds = transactions
            .SelectMany(t => t.Splits)
            .Select(s => s.CommodityId)
            .Distinct()
            .ToList();
        var commodities = await db.Commodities
            .AsNoTracking()
            .Where(c => commodityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        return transactions
            .Select(t =>
            {
                var accountSplit = OpeningBalanceMapping.AccountSideSplit(t, openingBalancesAccount.Id);
                return OpeningBalanceMapping.ToResponse(t, accountSplit, commodities[accountSplit.CommodityId]);
            })
            .OrderBy(r => r.CommodityId)
            .ToList();
    }
}
