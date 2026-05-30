using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public class PatchBalanceEntryHandler(
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchBalanceEntryCommand, BalanceEntryResponse>
{
    public async Task<BalanceEntryResponse> Handle(PatchBalanceEntryCommand request, CancellationToken ct)
    {
        var result = await balanceEntries.UpdateAsync(
            request.TransactionId,
            request.AccountId,
            new UpdateBalanceEntryRequest(
                request.CommodityId,
                request.AssertedAmount,
                request.Date,
                request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return BalanceEntryMapping.ToResponse(result.Entry!);
    }
}
