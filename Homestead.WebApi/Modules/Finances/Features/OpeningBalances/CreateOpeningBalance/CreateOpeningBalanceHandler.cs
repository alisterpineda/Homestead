using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public class CreateOpeningBalanceHandler(
    IOpeningBalanceService openingBalances,
    ISystemAccountProvider systemAccounts,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateOpeningBalanceCommand, OpeningBalanceResponse>
{
    public async Task<OpeningBalanceResponse> Handle(CreateOpeningBalanceCommand request, CancellationToken ct)
    {
        var transaction = await openingBalances.CreateAsync(
            new CreateOpeningBalanceRequest(
                request.AccountId, request.CommodityId, request.Quantity,
                request.ValuePerUnit, request.AsOfDate, request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);

        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
