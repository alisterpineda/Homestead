using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public class CreateOpeningBalanceHandler(
    AppDbContext db,
    IOpeningBalanceService openingBalances,
    ISystemAccountProvider systemAccounts)
    : IRequestHandler<CreateOpeningBalanceCommand, OpeningBalanceResponse>
{
    public async Task<OpeningBalanceResponse> Handle(CreateOpeningBalanceCommand request, CancellationToken ct)
    {
        var transaction = await openingBalances.CreateAsync(
            new CreateOpeningBalanceRequest(
                request.AccountId, request.CommodityId, request.Quantity,
                request.ValuePerUnit, request.AsOfDate, request.Memo),
            ct);

        await db.SaveChangesAsync(ct);

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);

        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
