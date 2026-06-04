using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.CreateDrip;

public class CreateDripHandler(
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateDripCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(CreateDripCommand request, CancellationToken ct)
    {
        var result = await drips.CreateAsync(
            new CreateDripRequest(
                request.AccountId,
                request.SecurityId,
                request.PaymentCurrencyId,
                request.Amount,
                request.Units,
                request.OccurredAt,
                request.Description),
            ct);

        // Flush so the reconciler's queries see both halves.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        // Return the Dividend half; its RelatedTransactionId points at the Buy half.
        return TransactionMapping.ToResponse(result.DividendHalf!);
    }
}
