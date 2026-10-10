using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;

public class DeleteTransactionHandler(
    AppDbContext db,
    ITransferService transfers,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
{
    public async Task Handle(DeleteTransactionCommand request, CancellationToken ct)
    {
        // Project just the discriminator: the shape services re-load the
        // tracked entity themselves, so we only need enough to dispatch.
        var probeType = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.Id == request.TransactionId)
            .Select(t => (TransactionType?)t.Type)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Transaction not found.");

        IReadOnlyCollection<TouchedPair> touched = probeType switch
        {
            TransactionType.BalanceEntry =>
                (await balanceEntries.DeleteAsync(request.TransactionId, ct)).TouchedPairs,
            TransactionType.OpeningBalance =>
                throw new BusinessRuleException(
                    "Opening-balance transactions must be deleted via the opening-balance endpoint."),
            TransactionType.Normal =>
                (await transfers.DeleteAsync(request.TransactionId, ct)).TouchedPairs,
            _ => throw new BusinessRuleException(
                $"Deletion is not supported for transactions of type {probeType}."),
        };

        await unitOfWork.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(touched, ct);
    }
}
