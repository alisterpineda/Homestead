using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;

public class PatchOpeningBalanceHandler(
    AppDbContext db,
    IOpeningBalanceService openingBalances,
    IBalanceEntryReconciler reconciler,
    ISystemAccountProvider systemAccounts,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchOpeningBalanceCommand, OpeningBalanceResponse>
{
    public async Task<OpeningBalanceResponse> Handle(PatchOpeningBalanceCommand request, CancellationToken ct)
    {
        var existingTxnId = await db.OpeningBalancesFor(request.AccountId, request.CommodityId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Opening balance not found for this account and commodity.");

        var result = await openingBalances.UpdateAsync(
            existingTxnId,
            new UpdateOpeningBalanceRequest(
                request.Quantity, request.ValuePerUnit, request.AsOfDate, request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        // Editing the earliest transaction on the chain shifts downstream balance entries' prior;
        // re-reconcile across both the old and new instants returned in TouchedPairs.
        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        var transaction = result.Transaction!;
        var openingBalancesAccount = await systemAccounts.GetOpeningBalancesAsync(ct);
        var accountSplit = OpeningBalanceMapping.AccountSideSplit(transaction, openingBalancesAccount.Id);

        return OpeningBalanceMapping.ToResponse(transaction, accountSplit);
    }
}
