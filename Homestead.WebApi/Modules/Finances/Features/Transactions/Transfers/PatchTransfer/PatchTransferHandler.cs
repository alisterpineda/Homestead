using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public class PatchTransferHandler(
    AppDbContext db,
    ITransferService transfers,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork,
    IValidator<PatchTransferCommand> validator)
{
    public async Task<TransactionResponse> HandleAsync(PatchTransferCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var result = await transfers.UpdateAsync(
            request.TransactionId,
            new UpdateTransferRequest(
                request.SourceAccountId,
                request.DestinationAccountId,
                request.SourceAmount,
                request.DestinationAmount,
                request.Date,
                request.Memo),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(
            result.Transaction!,
            documentLinkCounts: await DocumentLinkCounts.ForAsync(db, result.Transaction!.Id, ct));
    }
}
