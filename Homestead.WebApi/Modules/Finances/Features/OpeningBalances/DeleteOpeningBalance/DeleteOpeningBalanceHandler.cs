using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;

public class DeleteOpeningBalanceHandler(
    AppDbContext db,
    IOpeningBalanceService openingBalances,
    IBalanceEntryReconciler reconciler)
    : IRequestHandler<DeleteOpeningBalanceCommand, bool>
{
    public async Task<bool> Handle(DeleteOpeningBalanceCommand request, CancellationToken ct)
    {
        var txnId = await db.OpeningBalancesFor(request.AccountId, request.CommodityId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (txnId is null) return false;

        var touched = await openingBalances.DeleteAsync(txnId.Value, ct);
        await db.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(touched, ct);

        return true;
    }
}
