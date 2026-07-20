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

        // Client-authoritative category: the review page resolves each line's category (pre-filling from the
        // payee default when the setting is on, honouring overrides + explicit clears) and sends CategoryId
        // verbatim. Pre-validate the distinct non-null ids BEFORE the atomic materialize loop —
        // FlowCounterpartResolver THROWS on an archived / wrong-direction / non-category account, and inside
        // that loop the throw would detonate the whole commit. Batch-load the referenced accounts AsNoTracking
        // and assert each is a live category account whose type matches its line's direction; any failure →
        // 400 (per-line message). This covers non-category, wrong-direction, and archived-mid-review ids.
        var categoryIds = request.LineItems
            .Where(l => l.CategoryId is not null)
            .Select(l => l.CategoryId!.Value)
            .Distinct()
            .ToList();

        var categoriesById = categoryIds.Count == 0
            ? new Dictionary<Guid, Account>()
            : await db.Accounts.AsNoTracking()
                .Where(a => categoryIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

        foreach (var line in request.LineItems)
        {
            if (line.CategoryId is not Guid categoryId)
                continue;
            if (!categoriesById.TryGetValue(categoryId, out var category)
                || !IsUsableCategory(category, line.Direction))
                throw new BusinessRuleException(
                    $"Category is not a valid {line.Direction} category for this line.");
        }

        // Staged raw description per line (keyed by the stable ProposedFlow.Index). This — NOT the editable
        // Description memo — is the learning key, so editing the memo never corrupts what's learned.
        var stagedRawByIndex = staged.Proposal.LineItems
            .GroupBy(f => f.Index)
            .ToDictionary(g => g.Key, g => g.First().Description);

        // Materialize each curated line as an Expense/Income flow. Memo is the (possibly edited) Description;
        // category is the client-supplied, pre-validated CategoryId or null (Uncategorised). CreateAsync is
        // save-free and per-transaction, so the whole set lands in one flush below.
        foreach (var line in request.LineItems)
        {
            var flow = await flows.CreateAsync(
                new CreateFlowRequest(
                    line.Direction,
                    staged.AccountId,
                    line.Amount,
                    line.Date,
                    line.Description,
                    CategoryId: line.CategoryId,
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

    // A client-supplied category is only accepted when it's a live category account whose type matches the
    // line's direction. Mirrors FlowCounterpartResolver's own checks — pre-validated here so an archived /
    // mismatched / non-category id fails with a clean 400 instead of throwing inside the atomic commit.
    private static bool IsUsableCategory(Account category, FlowDirection direction)
    {
        if (category.IsArchived || !category.AccountType.IsCategory())
            return false;

        var expected = direction == FlowDirection.Expense ? AccountType.Expense : AccountType.Income;
        return category.AccountType == expected;
    }
}
