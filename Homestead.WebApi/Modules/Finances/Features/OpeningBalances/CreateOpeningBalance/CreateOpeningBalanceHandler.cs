using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public class CreateOpeningBalanceHandler(
    AppDbContext db,
    IOpeningBalanceService openingBalances,
    ISystemAccountProvider systemAccounts)
    : IRequestHandler<CreateOpeningBalanceCommand, OpeningBalanceResponse>
{
    public async Task<OpeningBalanceResponse> Handle(CreateOpeningBalanceCommand request, CancellationToken ct)
    {
        var commodity = await db.Commodities.FirstOrDefaultAsync(c => c.Id == request.CommodityId, ct)
            ?? throw new BusinessRuleException("Commodity not found.");

        var transaction = await openingBalances.CreateAsync(
            request.AccountId, request.CommodityId, request.Quantity,
            request.ValuePerUnit, request.AsOfDate, ct);

        await db.SaveChangesAsync(ct);

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);

        return OpeningBalanceMapping.ToResponse(transaction, accountSplit, commodity);
    }
}
