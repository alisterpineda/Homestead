using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public class PatchDividendHandler(
    AppDbContext db,
    IDividendService dividends,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDividendCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(PatchDividendCommand request, CancellationToken ct)
    {
        var result = await dividends.UpdateAsync(
            request.TransactionId,
            new UpdateDividendRequest(
                request.AccountId,
                request.PaymentCurrencyId,
                request.Amount,
                request.OccurredAt,
                request.Description),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(
            result.Transaction!,
            documentLinkCounts: await DocumentLinkCounts.ForAsync(db, result.Transaction!.Id, ct));
    }
}
