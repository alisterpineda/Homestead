using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public class PatchDividendHandler(
    AppDbContext db,
    IDividendService dividends,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<PatchDividendCommand> validator)
{
    public async Task<TransactionResponse> HandleAsync(PatchDividendCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

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
