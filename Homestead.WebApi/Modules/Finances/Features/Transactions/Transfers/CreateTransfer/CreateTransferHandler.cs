using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public class CreateTransferHandler(
    ITransferService transfers,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<CreateTransferCommand> validator)
{
    public async Task<TransactionResponse> Handle(CreateTransferCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var result = await transfers.CreateAsync(
            new CreateTransferRequest(
                request.SourceAccountId,
                request.DestinationAccountId,
                request.SourceAmount,
                request.DestinationAmount,
                request.Date,
                request.Memo),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new transfer.
        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
