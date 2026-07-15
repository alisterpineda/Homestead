using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

/// <summary>
/// Convenience undo: deletes every ledger transaction stamped with the given source document version. After
/// Feature B a single import can produce a mixed set — one balance-entry assertion plus N Expense/Income
/// flows — so deletion is dispatched by transaction type (mirroring <c>DeleteTransactionHandler</c>):
/// <c>BalanceEntry → IBalanceEntryService.DeleteAsync</c>, <c>Normal → ITransferService.DeleteAsync</c>.
/// Both emit touched pairs; one flush + one reconcile re-derives downstream balance entries.
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
        var rows = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.SourceDocumentVersionId == request.SourceDocumentVersionId)
            .Select(t => new { t.Id, t.Type })
            .ToListAsync(ct);

        if (rows.Count == 0)
            throw new EntityNotFoundException("No imported transactions found for this document version.");

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
