using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public class PatchBalanceEntryHandler(
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<PatchBalanceEntryCommand> validator)
{
    public async Task<BalanceEntryResponse> Handle(PatchBalanceEntryCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

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
