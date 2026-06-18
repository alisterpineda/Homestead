using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.DeleteOpeningBalance;

public class DeleteOpeningBalanceHandler(
    AppDbContext db,
    IOpeningBalanceService openingBalances,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteOpeningBalanceCommand, bool>
{
    public async Task<bool> Handle(DeleteOpeningBalanceCommand request, CancellationToken ct)
    {
        var txnId = await db.OpeningBalancesFor(request.AccountId, request.CommodityId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        if (txnId is null) return false;

        var result = await openingBalances.DeleteAsync(txnId.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return true;
    }
}
