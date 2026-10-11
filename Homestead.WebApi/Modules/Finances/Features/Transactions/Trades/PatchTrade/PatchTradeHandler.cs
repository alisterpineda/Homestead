using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.PatchTrade;

public class PatchTradeHandler(
    AppDbContext db,
    ITradeService trades,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<PatchTradeCommand> validator)
{
    public async Task<TransactionResponse> HandleAsync(PatchTradeCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

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

        return TransactionMapping.ToResponse(
            result.Transaction!,
            documentLinkCounts: await DocumentLinkCounts.ForAsync(db, result.Transaction!.Id, ct));
    }
}
