using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public class PatchBalanceEntryHandler(
    AppDbContext db,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler)
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
                request.Note),
            ct);

        await db.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return BalanceEntryMapping.ToResponse(result.Entry!);
    }
}
