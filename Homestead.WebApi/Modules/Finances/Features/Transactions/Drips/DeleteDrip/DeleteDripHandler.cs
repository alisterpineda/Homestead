using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.DeleteDrip;

public class DeleteDripHandler(
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteDripCommand, Unit>
{
    public async Task<Unit> Handle(DeleteDripCommand request, CancellationToken ct)
    {
        var result = await drips.DeleteBothAsync(request.TransactionId, ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return Unit.Value;
    }
}
