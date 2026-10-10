using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.BulkDeleteTransactions;

/// <summary>
/// Deletes a user-selected set of transactions in one atomic command, dispatching by transaction
/// type (mirroring <c>DeleteTransactionHandler</c> / <c>RollbackStatementImportHandler</c>):
/// <c>BalanceEntry → IBalanceEntryService.DeleteAsync</c>, <c>Normal → ITransferService.DeleteAsync</c>.
/// A DRIP half (non-null <c>RelatedTransactionId</c>) routes to <c>IDripService.DeleteBothAsync</c>,
/// removing the partner even when it wasn't requested — the pair is one logical event. Opening
/// balances are rejected outright (their dedicated endpoint owns delete + re-reconcile semantics).
/// Nonexistent ids are skipped; all deletes emit touched pairs; one flush + one reconcile.
/// </summary>
public class BulkDeleteTransactionsHandler(
    AppDbContext db,
    ITransferService transfers,
    IBalanceEntryService balanceEntries,
    IDripService drips,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
{
    public async Task<BulkDeleteTransactionsResponse> Handle(
        BulkDeleteTransactionsCommand request, CancellationToken ct)
    {
        // Project just id + discriminator + DRIP link: the shape services re-load the tracked
        // entity themselves. The IN-query dedups duplicate request ids by construction; the
        // `processed` set below covers the one real dedup case (both DRIP halves requested).
        var rows = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => request.Ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Type, t.RelatedTransactionId })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new BulkDeleteTransactionsResponse(0);

        var processed = new HashSet<Guid>();
        var touched = new List<TouchedPair>();
        var deleted = 0;

        foreach (var row in rows)
        {
            if (!processed.Add(row.Id))
                continue;

            switch (row.Type)
            {
                case TransactionType.OpeningBalance:
                    throw new BusinessRuleException(
                        "Opening-balance transactions must be deleted via the opening-balance endpoint.");

                case TransactionType.BalanceEntry:
                    touched.AddRange((await balanceEntries.DeleteAsync(row.Id, ct)).TouchedPairs);
                    deleted += 1;
                    break;

                case TransactionType.Normal when row.RelatedTransactionId is Guid partnerId:
                    touched.AddRange((await drips.DeleteBothAsync(row.Id, ct)).TouchedPairs);
                    processed.Add(partnerId);
                    deleted += 2;
                    break;

                case TransactionType.Normal:
                    touched.AddRange((await transfers.DeleteAsync(row.Id, ct)).TouchedPairs);
                    deleted += 1;
                    break;

                default:
                    throw new BusinessRuleException(
                        $"Deletion is not supported for transactions of type {row.Type}.");
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        await reconciler.ReconcileAsync(touched, ct);

        return new BulkDeleteTransactionsResponse(deleted);
    }
}
