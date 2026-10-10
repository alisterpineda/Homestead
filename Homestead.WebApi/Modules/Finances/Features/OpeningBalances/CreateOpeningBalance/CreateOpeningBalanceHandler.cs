using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public class CreateOpeningBalanceHandler(
    IOpeningBalanceService openingBalances,
    IBalanceEntryReconciler reconciler,
    ISystemAccountProvider systemAccounts,
    IUnitOfWork unitOfWork,
    IValidator<CreateOpeningBalanceCommand> validator)
{
    public async Task<OpeningBalanceResponse> Handle(CreateOpeningBalanceCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var result = await openingBalances.CreateAsync(
            new CreateOpeningBalanceRequest(
                request.AccountId, request.CommodityId, request.Quantity,
                request.ValuePerUnit, request.AsOfDate, request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        // An opening balance is the earliest transaction on its chain; re-reconcile any pre-existing
        // downstream balance entries whose prior-running it shifts.
        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var transaction = result.Transaction!;
        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);

        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
