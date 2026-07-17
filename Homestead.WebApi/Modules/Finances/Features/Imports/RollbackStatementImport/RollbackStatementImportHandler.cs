using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

/// <summary>
/// Convenience undo: deletes every ledger transaction holding an import-origin document link to the given
/// document. After Feature B a single import can produce a mixed set — one balance-entry assertion plus N
/// Expense/Income flows — so deletion is dispatched by transaction type (mirroring
/// <c>DeleteTransactionHandler</c>): <c>BalanceEntry → IBalanceEntryService.DeleteAsync</c>,
/// <c>Normal → ITransferService.DeleteAsync</c>. The links themselves cascade away with the transactions.
/// Both services emit touched pairs; one flush + one reconcile re-derives downstream balance entries.
/// </summary>
public class RollbackStatementImportHandler(
    AppDbContext db,
    IBalanceEntryService balanceEntries,
    ITransferService transfers,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RollbackStatementImportCommand, RollbackStatementImportResponse>
{
    public async Task<RollbackStatementImportResponse> Handle(
        RollbackStatementImportCommand request, CancellationToken ct)
    {
        // Project just id + discriminator: the shape services re-load the tracked entity themselves.
        // IsImportOriginFor keeps this scope in lockstep with the initiate/commit guards — manual
        // links to the same document must not drag their transactions into a rollback.
        var rows = await db.TransactionDocumentLinks
            .AsNoTracking()
            .Where(TransactionDocumentLink.IsImportOriginFor(request.DocumentId))
            .Select(l => new { Id = l.AccountTransactionId, l.AccountTransaction.Type })
            .ToListAsync(ct);

        if (rows.Count == 0)
            throw new EntityNotFoundException("No imported transactions found for this document.");

        var touched = new List<TouchedPair>();
        foreach (var row in rows)
        {
            var result = row.Type switch
            {
                TransactionType.BalanceEntry => (await balanceEntries.DeleteAsync(row.Id, ct)).TouchedPairs,
                TransactionType.Normal => (await transfers.DeleteAsync(row.Id, ct)).TouchedPairs,
                _ => throw new BusinessRuleException(
                    $"Rollback is not supported for transactions of type {row.Type}."),
            };
            touched.AddRange(result);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await reconciler.ReconcileAsync(touched, ct);

        return new RollbackStatementImportResponse { DeletedCount = rows.Count };
    }
}
