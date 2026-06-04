using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.PatchDrip;

public class PatchDripHandler(
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDripCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(PatchDripCommand request, CancellationToken ct)
    {
        var result = await drips.UpdateAsync(
            request.TransactionId,
            new UpdateDripRequest(
                request.AccountId,
                request.SecurityId,
                request.PaymentCurrencyId,
                request.Amount,
                request.Units,
                request.OccurredAt,
                request.Description),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.DividendHalf!);
    }
}
