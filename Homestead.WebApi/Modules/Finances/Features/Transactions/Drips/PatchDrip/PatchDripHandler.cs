using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.PatchDrip;

public class PatchDripHandler(
    AppDbContext db,
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<PatchDripCommand> validator)
{
    public async Task<TransactionResponse> Handle(PatchDripCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

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

        return TransactionMapping.ToResponse(
            result.DividendHalf!,
            documentLinkCounts: await DocumentLinkCounts.ForAsync(db, result.DividendHalf!.Id, ct));
    }
}
