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
    IFlowService flows,
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

        // Reviewer accepted neither the assertion nor any lines — nothing to persist.
        if (!request.Accepted && request.LineItems.Count == 0)
            return new CommitStatementImportResponse
            {
                Committed = false,
                EntryId = null,
                FlowsCreated = 0,
                AccountId = staged.AccountId,
            };

        // Re-commit guard: any import-origin link to this document means the statement was already imported
        // (Feature A + B commit together atomically, so a single link row — balance entry or flow — closes
        // the whole document; one document = one active import). Origin-scoped so a *manual* link to the
        // statement never blocks importing it. Initiate carries the same cheap pre-check before the metered
        // AI parse; this one is authoritative. The per-day balance-entry unique index is the second backstop
        // inside CreateAsync's EnsureNoExistingEntryAsync, which re-checks the live ledger.
        var alreadyImported = await db.TransactionDocumentLinks
            .AsNoTracking()
            .AnyAsync(TransactionDocumentLink.IsImportOriginFor(staged.DocumentId), ct);
        if (alreadyImported)
            throw new BusinessRuleException("This statement has already been imported.");

        // Accumulate every created transaction (balance entry + flows) and their touched pairs, so a single
        // flush + single reconcile covers the batch (the reconciler collapses overlapping pairs). Mirrors the
        // flush-then-reconcile precedent in ApplyCommodityPriceObservationsCommand / CreateBalanceEntryHandler.
        var created = new List<AccountTransaction>();
        var touched = new List<TouchedPair>();
        Guid? entryId = null;

        if (request.Accepted)
        {
            var account = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == staged.AccountId, ct)
                ?? throw new EntityNotFoundException("Account not found.");

            // Assertion in the account's reporting currency (CreateAsync re-validates writability and
            // commodity, and rejects a same-day duplicate against current ledger state).
            var result = await balanceEntries.CreateAsync(
                new CreateBalanceEntryRequest(
                    staged.AccountId,
                    account.ReportingCurrencyId,
                    request.AssertedAmount,
                    request.Date,
                    request.Memo),
                ct);

            created.Add(result.Entry!);
            touched.AddRange(result.TouchedPairs);
            entryId = result.Entry!.Id;
        }

        // Materialize each curated line as an Expense/Income flow, uncategorised (CategoryId: null → the
        // system Uncategorised bucket). CreateAsync is save-free and per-transaction, so the whole set lands
        // in one flush below.
        foreach (var line in request.LineItems)
        {
            var flow = await flows.CreateAsync(
                new CreateFlowRequest(
                    line.Direction,
                    staged.AccountId,
                    line.Amount,
                    line.Date,
                    line.Description,
                    CategoryId: null),
                ct);

            created.Add(flow.Transaction!);
            touched.AddRange(flow.TouchedPairs);
        }

        // Import-origin provenance: one document-grained link per created (tracked) transaction, added
        // before the flush so links land in the same command transaction as the transactions themselves.
        foreach (var transaction in created)
            db.TransactionDocumentLinks.Add(new TransactionDocumentLink
            {
                AccountTransaction = transaction,
                DocumentId = staged.DocumentId,
                Origin = TransactionDocumentLinkOrigin.StatementImport,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = currentUser.UserId,
            });

        // Canonical flush-then-reconcile order — one flush, one reconcile over the combined touched set.
        await unitOfWork.SaveChangesAsync(ct);
        await reconciler.ReconcileAsync(touched, ct);

        return new CommitStatementImportResponse
        {
            Committed = entryId is not null,
            EntryId = entryId,
            FlowsCreated = request.LineItems.Count,
            AccountId = staged.AccountId,
        };
    }
}
