using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.Settings;
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
    IPayeeMappingService payeeMappings,
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

        // Auto-category pre-filter: a payee's default category rides along to also categorise the line, but
        // only when the whole path is valid. FlowCounterpartResolver THROWS on an archived / wrong-direction
        // category, which inside this atomic loop would detonate the entire commit — so we pre-validate and
        // pass CategoryId only when the category exists, is a category account, isn't archived, and its type
        // matches the line's direction; otherwise null (Uncategorised). Gated by the AutoCategoriseFromPayee
        // setting (default on).
        var categoryByPayee = await LoadAutoCategoriesAsync(request.LineItems, ct);

        // Staged raw description per line (keyed by the stable ProposedFlow.Index). This — NOT the editable
        // Description memo — is the learning key, so editing the memo never corrupts what's learned.
        var stagedRawByIndex = staged.Proposal.LineItems
            .GroupBy(f => f.Index)
            .ToDictionary(g => g.Key, g => g.First().Description);

        // Materialize each curated line as an Expense/Income flow. Memo is the (possibly edited) Description;
        // category is the pre-validated auto-category or null. CreateAsync is save-free and per-transaction,
        // so the whole set lands in one flush below.
        foreach (var line in request.LineItems)
        {
            Guid? categoryId = null;
            if (line.PayeeId is Guid pid
                && categoryByPayee.TryGetValue(pid, out var category)
                && IsUsableCategory(category, line.Direction))
                categoryId = category.Id;

            var flow = await flows.CreateAsync(
                new CreateFlowRequest(
                    line.Direction,
                    staged.AccountId,
                    line.Amount,
                    line.Date,
                    line.Description,
                    CategoryId: categoryId,
                    PayeeId: line.PayeeId),
                ct);

            created.Add(flow.Transaction!);
            touched.AddRange(flow.TouchedPairs);
        }

        // Learn only on an explicit signal (Learn && a payee was set): a rubber-stamped tier-① pre-fill or a
        // warning-dialog auto-apply arrives with Learn=false and is applied but not taught. Key on the staged
        // raw text; a null-normalizing line (boilerplate / degenerate) is silently skipped. De-dupe within
        // the batch (last-wins per key) so an intra-commit repeat bumps TimesConfirmed exactly once.
        var learnByKey = new Dictionary<string, (Guid PayeeId, string SampleRaw)>();
        foreach (var line in request.LineItems)
        {
            if (!line.Learn || line.PayeeId is not Guid learnPayeeId)
                continue;
            // A manual line has no staged origin (null index) → no raw text to key on, so it never learns.
            if (line.Index is not int idx)
                continue;
            if (!stagedRawByIndex.TryGetValue(idx, out var stagedRaw))
                continue;
            var key = PayeeDescriptionNormalizer.Normalize(stagedRaw);
            if (key is null)
                continue;
            learnByKey[key] = (learnPayeeId, stagedRaw!);
        }

        // Upsert BEFORE the flush so the learning writes are atomic with the flows (the interceptor enrolls
        // each raw ON CONFLICT into the same command transaction; any rollback retracts both).
        foreach (var (key, entry) in learnByKey)
            await payeeMappings.UpsertAsync(key, entry.PayeeId, entry.SampleRaw, staged.DocumentId, ct);

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

    /// <summary>
    /// Batch-load the default-category <see cref="Account"/> of each payee referenced by a line, keyed by
    /// payee id. Empty when auto-categorise is off or no line carries a payee — so the per-line decision is a
    /// dictionary lookup, not a query. Only payees that actually have a default category appear.
    /// </summary>
    private async Task<Dictionary<Guid, Account>> LoadAutoCategoriesAsync(
        IReadOnlyList<CommitLineItem> lines, CancellationToken ct)
    {
        var payeeIds = lines
            .Where(l => l.PayeeId is not null)
            .Select(l => l.PayeeId!.Value)
            .Distinct()
            .ToList();

        if (payeeIds.Count == 0)
            return new Dictionary<Guid, Account>();

        var settings = await FinanceSettings.LoadAsync(db, ct);
        if (!settings.AutoCategoriseFromPayeeEnabled)
            return new Dictionary<Guid, Account>();

        return await db.Payees
            .AsNoTracking()
            .Where(p => payeeIds.Contains(p.Id) && p.DefaultCategoryId != null)
            .Select(p => new { p.Id, Category = p.DefaultCategory! })
            .ToDictionaryAsync(x => x.Id, x => x.Category, ct);
    }

    // A payee's default category is only applied when it's a live category account whose type matches the
    // line's direction. Mirrors FlowCounterpartResolver's own checks — pre-filtered here so an archived /
    // mismatched category degrades to Uncategorised instead of throwing inside the atomic commit.
    private static bool IsUsableCategory(Account category, FlowDirection direction)
    {
        if (category.IsArchived || !category.AccountType.IsCategory())
            return false;

        var expected = direction == FlowDirection.Expense ? AccountType.Expense : AccountType.Income;
        return category.AccountType == expected;
    }
}
