using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public class CreateBalanceEntryHandler(
    AppDbContext db,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler)
    : IRequestHandler<CreateBalanceEntryCommand, BalanceEntryResponse>
{
    public async Task<BalanceEntryResponse> Handle(CreateBalanceEntryCommand request, CancellationToken ct)
    {
        var result = await balanceEntries.CreateAsync(
            new CreateBalanceEntryRequest(
                request.AccountId,
                request.CommodityId,
                request.AssertedAmount,
                request.Date,
                request.Description),
            ct);

        await db.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return BalanceEntryMapping.ToResponse(result.Entry!);
    }
}
