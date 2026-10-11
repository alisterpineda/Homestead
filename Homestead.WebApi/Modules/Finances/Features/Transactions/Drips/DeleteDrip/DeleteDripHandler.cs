using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

public class DeleteDripHandler(
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(Guid transactionId, CancellationToken ct)
    {
        var result = await drips.DeleteBothAsync(transactionId, ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);
    }
}
