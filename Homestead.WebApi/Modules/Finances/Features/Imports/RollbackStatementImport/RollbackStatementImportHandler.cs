using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

/// <summary>
/// Convenience undo: deletes every ledger transaction stamped with the given source document version. In
/// Phase 1 that is exactly one balance-entry assertion; deletion flows through
/// <see cref="IBalanceEntryService.DeleteAsync"/> so it emits touched pairs (reconcile) and audits.
/// </summary>
public class RollbackStatementImportHandler(
    AppDbContext db,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RollbackStatementImportCommand, RollbackStatementImportResponse>
{
    public async Task<RollbackStatementImportResponse> Handle(
        RollbackStatementImportCommand request, CancellationToken ct)
    {
        var transactionIds = await db.AccountTransactions
            .AsNoTracking()
            .Where(t => t.SourceDocumentVersionId == request.SourceDocumentVersionId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (transactionIds.Count == 0)
            throw new EntityNotFoundException("No imported transactions found for this document version.");

        var touched = new List<TouchedPair>();
        foreach (var id in transactionIds)
        {
            var result = await balanceEntries.DeleteAsync(id, ct);
            touched.AddRange(result.TouchedPairs);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await reconciler.ReconcileAsync(touched, ct);

        return new RollbackStatementImportResponse { DeletedCount = transactionIds.Count };
    }
}
