using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.DeleteTransaction;

public class DeleteTransactionHandler(
    AppDbContext db,
    ITransferService transfers,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler)
    : IRequestHandler<DeleteTransactionCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTransactionCommand request, CancellationToken ct)
    {
        // Project just the discriminator + description: the shape services re-load the
        // tracked entity themselves, so we only need enough to dispatch.
        var probe = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.Id == request.TransactionId)
            .Select(t => new { t.Type, t.Description })
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("Transaction not found.");

        IReadOnlyCollection<TouchedPair> touched = probe.Type switch
        {
            TransactionType.BalanceEntry =>
                (await balanceEntries.DeleteAsync(request.TransactionId, ct)).TouchedPairs,
            TransactionType.Normal when probe.Description == OpeningBalanceTransactions.Description =>
                throw new BusinessRuleException(
                    "Opening-balance transactions must be deleted via the opening-balance endpoint."),
            TransactionType.Normal =>
                (await transfers.DeleteAsync(request.TransactionId, ct)).TouchedPairs,
            TransactionType.StockSplit => throw new BusinessRuleException(
                "Stock-split transaction deletion is not supported in v1."),
            _ => throw new BusinessRuleException(
                $"Deletion is not supported for transactions of type {probe.Type}."),
        };

        await db.SaveChangesAsync(ct);

        await reconciler.ReconcileAsync(touched, ct);

        return Unit.Value;
    }
}
