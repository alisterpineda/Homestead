using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public class CreateTransferHandler(
    AppDbContext db,
    ITransferService transfers,
    IBalanceEntryReconciler reconciler)
    : IRequestHandler<CreateTransferCommand, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(CreateTransferCommand request, CancellationToken ct)
    {
        var result = await transfers.CreateAsync(
            new CreateTransferRequest(
                request.SourceAccountId,
                request.DestinationAccountId,
                request.SourceAmount,
                request.DestinationAmount,
                request.Date,
                request.Description),
            ct);

        // Flush the primary change so the reconciler's DB queries see the new transfer.
        await db.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return TransactionMapping.ToResponse(result.Transaction!);
    }
}
