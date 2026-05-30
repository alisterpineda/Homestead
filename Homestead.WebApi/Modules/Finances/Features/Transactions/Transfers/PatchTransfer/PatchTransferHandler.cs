using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public class PatchTransferHandler(
    ITransferService transfers,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchTransferCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(PatchTransferCommand request, CancellationToken ct)
    {
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

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
