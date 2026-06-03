using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.CreateDividend;

public class CreateDividendHandler(
    IDividendService dividends,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateDividendCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(CreateDividendCommand request, CancellationToken ct)
    {
        var result = await dividends.CreateAsync(
            new CreateDividendRequest(
                request.AccountId,
                request.PaymentCurrencyId,
                request.Amount,
                request.OccurredAt,
                request.Description),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new dividend.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
