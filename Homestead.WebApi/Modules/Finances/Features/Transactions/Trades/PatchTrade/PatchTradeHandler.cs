using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.PatchTrade;

public class PatchTradeHandler(
    ITradeService trades,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchTradeCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(PatchTradeCommand request, CancellationToken ct)
    {
        var result = await trades.UpdateAsync(
            request.TransactionId,
            new UpdateTradeRequest(
                request.AccountId,
                request.Side,
                request.SecurityId,
                request.FundingCurrencyId,
                request.Quantity,
                request.UnitPrice,
                request.OccurredAt,
                request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
