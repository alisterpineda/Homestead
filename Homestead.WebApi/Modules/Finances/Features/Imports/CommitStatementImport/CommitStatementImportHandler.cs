using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public class CommitStatementImportHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IBalanceEntryService balanceEntries,
    IBalanceEntryReconciler reconciler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CommitStatementImportCommand, CommitStatementImportResponse>
{
    public async Task<CommitStatementImportResponse> Handle(
        CommitStatementImportCommand request, CancellationToken ct)
    {
        var job = await db.ProcessingJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct);

        if (job is null
            || job.Type != ProcessingJobType.ParseAccountStatement
            || job.CreatedByUserId != currentUser.UserId)
            throw new EntityNotFoundException("Statement import not found.");

        var staged = job.GetResult<StagedStatementImport>()
            ?? throw new BusinessRuleException("Statement import is not ready to commit.");

        // Reviewer declined the assertion — nothing to persist.
        if (!request.Accepted)
            return new CommitStatementImportResponse
            {
                Committed = false,
                EntryId = null,
                AccountId = staged.AccountId,
            };

        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == staged.AccountId, ct)
            ?? throw new EntityNotFoundException("Account not found.");

        // Re-commit guard: an entry already provenance-stamped from this exact version means this statement
        // was already imported. (The per-day balance-entry unique index is the second backstop, inside
        // CreateAsync's EnsureNoExistingEntryAsync, which also re-checks dedup against the live ledger.)
        var alreadyImported = await db.AccountTransactions
            .AsNoTracking()
            .AnyAsync(t => t.SourceDocumentVersionId == staged.DocumentVersionId, ct);
        if (alreadyImported)
            throw new BusinessRuleException("This statement version has already been imported.");

        // Create the assertion in the account's reporting currency (CreateAsync re-validates writability and
        // commodity, and rejects a same-day duplicate against current ledger state).
        var result = await balanceEntries.CreateAsync(
            new CreateBalanceEntryRequest(
                staged.AccountId,
                account.ReportingCurrencyId,
                request.AssertedAmount,
                request.Date,
                request.Memo),
            ct);

        // Stamp soft provenance on the tracked entity before the flush.
        result.Entry!.SourceDocumentVersionId = staged.DocumentVersionId;

        // Canonical flush-then-reconcile order (see CreateBalanceEntryHandler).
        await unitOfWork.SaveChangesAsync(ct);
        await reconciler.ReconcileAsync(result.TouchedPairs, ct);

        return new CommitStatementImportResponse
        {
            Committed = true,
            EntryId = result.Entry.Id,
            AccountId = staged.AccountId,
        };
    }
}
