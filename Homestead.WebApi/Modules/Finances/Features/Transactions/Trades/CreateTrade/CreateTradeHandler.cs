using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.CreateTrade;

public class CreateTradeHandler(
    ITradeService trades,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTradeCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(CreateTradeCommand request, CancellationToken ct)
    {
        var result = await trades.CreateAsync(
            new CreateTradeRequest(
                request.AccountId,
                request.Side,
                request.SecurityId,
                request.FundingCurrencyId,
                request.Quantity,
                request.UnitPrice,
                request.OccurredAt,
                request.Memo),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new trade.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
