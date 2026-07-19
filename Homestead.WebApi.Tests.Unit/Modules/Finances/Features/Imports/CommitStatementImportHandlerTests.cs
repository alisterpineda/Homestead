using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Imports;

// Note: a "commodity mismatch" is not reachable through commit — the handler always asserts in the
// account's reporting currency, so ValidateCommodityForAccount (covered by BalanceEntryServiceTests)
// never trips here. The meaningful commit rejections are ownership, not-accepted, and re-import dedup.
public class CommitStatementImportHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ICurrentUserAccessor _currentUser = null!;
    private IBalanceEntryReconciler _reconciler = null!;
    private IUnitOfWork _unitOfWork = null!;
    private IPayeeMappingService _payeeMappings = null!;
    private BalanceEntryService _balanceEntries = null!;
    private FlowService _flows = null!;
    private CommitStatementImportHandler _sut = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private Commodity _cad = null!;
    private Account _account = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);

        var adjustments = new Account
        {
            Id = Guid.NewGuid(), Name = "Equity:Adjustments", AccountType = AccountType.Equity,
            ReportingCurrencyId = _cad.Id, IsSystem = true,
        };
        var expense = new Account
        {
            Id = Guid.NewGuid(), Name = "Expense", AccountType = AccountType.Expense,
            ReportingCurrencyId = _cad.Id, IsSystem = true,
        };
        var income = new Account
        {
            Id = Guid.NewGuid(), Name = "Income", AccountType = AccountType.Income,
            ReportingCurrencyId = _cad.Id, IsSystem = true,
        };
        _account = new Account
        {
            Id = Guid.NewGuid(), Name = "Chequing", AccountType = AccountType.Asset, Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id, IsSystem = false, IsArchived = false,
        };
        _db.Accounts.AddRange(adjustments, expense, income, _account);
        await _db.SaveChangesAsync();

        var systemAccounts = Substitute.For<ISystemAccountProvider>();
        systemAccounts.GetAdjustmentsAsync(Arg.Any<CancellationToken>()).Returns(adjustments);

        var counterparts = Substitute.For<IFlowCounterpartResolver>();
        counterparts.ResolveAsync(FlowDirection.Expense, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(expense);
        counterparts.ResolveAsync(FlowDirection.Income, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(income);

        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);

        _reconciler = Substitute.For<IBalanceEntryReconciler>();

        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _payeeMappings = Substitute.For<IPayeeMappingService>();
        _payeeMappings.UpsertAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _balanceEntries = new BalanceEntryService(
            _db, systemAccounts, new TransactionValidator(_db), Substitute.For<IAuditContext>());
        _flows = new FlowService(
            _db, counterparts, new TransactionValidator(_db), Substitute.For<IAuditContext>());

        _sut = new CommitStatementImportHandler(
            _db, _currentUser, _balanceEntries, _flows, _reconciler, _payeeMappings, _unitOfWork);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<(ProcessingJob job, Guid documentId)> SeedJobAsync(Guid? owner = null)
    {
        var documentId = Guid.NewGuid();
        var staged = new StagedStatementImport
        {
            AccountId = _account.Id,
            DocumentId = documentId,
            DocumentVersionId = Guid.NewGuid(),
            Ir = new StatementIr { Kind = StatementKind.Bank, Header = new StatementHeaderIr() },
            Proposal = new StatementMappingProposal { AccountNumberMatch = AccountNumberMatch.Unknown },
        };
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ParseAccountStatement,
            Status = ProcessingJobStatus.Completed,
            CreatedByUserId = owner ?? _userId,
        };
        job.SetResult(staged);
        _db.ProcessingJobs.Add(job);
        await _db.SaveChangesAsync();
        return (job, documentId);
    }

    /// <summary>Seeds a prior imported/linked transaction so guard behaviour can be exercised.</summary>
    private async Task SeedLinkedTransactionAsync(Guid documentId, TransactionDocumentLinkOrigin origin)
    {
        var prior = new BalanceEntryTransaction
        {
            Id = Guid.NewGuid(), OccurredAt = new DateOnly(2026, 5, 31).ToEndOfDayUtc(), Memo = "prior",
            Type = TransactionType.BalanceEntry, AccountId = _account.Id, AssertedCommodityId = _cad.Id,
            AssertedQuantity = 1m,
        };
        _db.AccountTransactions.Add(prior);
        _db.TransactionDocumentLinks.Add(new TransactionDocumentLink
        {
            AccountTransactionId = prior.Id,
            DocumentId = documentId,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private static CommitStatementImportCommand Command(Guid jobId, bool accepted = true) => new()
    {
        JobId = jobId,
        AssertedAmount = 1500m,
        Date = new DateOnly(2026, 6, 30),
        Memo = "June statement",
        Accepted = accepted,
    };

    [Fact]
    public async Task Commit_Accepted_CreatesEntry_WritesImportLink_AndReconciles()
    {
        var (job, documentId) = await SeedJobAsync();

        var result = await _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken);

        Assert.True(result.Committed);
        Assert.NotNull(result.EntryId);
        Assert.Equal(_account.Id, result.AccountId);

        var entry = await _db.AccountTransactions.OfType<BalanceEntryTransaction>()
            .SingleAsync(e => e.Id == result.EntryId);
        Assert.Equal(_account.Id, entry.AccountId);
        Assert.Equal(_cad.Id, entry.AssertedCommodityId);

        var link = await _db.TransactionDocumentLinks
            .SingleAsync(l => l.AccountTransactionId == entry.Id);
        Assert.Equal(documentId, link.DocumentId);
        Assert.Equal(TransactionDocumentLinkOrigin.StatementImport, link.Origin);
        Assert.Equal(_userId, link.CreatedByUserId);

        await _reconciler.Received(1).ReconcileAsync(
            Arg.Any<IEnumerable<TouchedPair>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_Accepted_WithLines_CreatesEntryAndFlows_AllLinked_OneReconcile()
    {
        var (job, documentId) = await SeedJobAsync();

        var command = Command(job.Id) with
        {
            LineItems =
            [
                new CommitLineItem(0, FlowDirection.Expense, 42.10m, new DateOnly(2026, 6, 3), "GROCERY"),
                new CommitLineItem(1, FlowDirection.Income, 2000m, new DateOnly(2026, 6, 15), "PAYROLL"),
            ],
        };

        var result = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.Committed);
        Assert.NotNull(result.EntryId);
        Assert.Equal(2, result.FlowsCreated);

        // Balance entry + 2 flows, each carrying an import-origin link to the statement's document.
        var links = await _db.TransactionDocumentLinks
            .Where(l => l.DocumentId == documentId
                     && l.Origin == TransactionDocumentLinkOrigin.StatementImport)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, links.Count);
        Assert.Equal(3, links.Select(l => l.AccountTransactionId).Distinct().Count());

        var linkedIds = links.Select(l => l.AccountTransactionId).ToList();
        var linked = await _db.AccountTransactions
            .Where(t => linkedIds.Contains(t.Id))
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(linked.OfType<BalanceEntryTransaction>());
        Assert.Equal(2, linked.OfType<NormalTransaction>().Count());

        // One combined reconcile pass over the whole batch's touched pairs.
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Any<IEnumerable<TouchedPair>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_ManualLine_NullIndex_CreatesFlow_AndIsNeverLearned_EvenWhenLearnTrue()
    {
        // A staged line 0 exists, but the reviewer also adds a *manual* line (Index=null) with a payee and
        // Learn=true. The manual line must materialize as a flow yet never teach the mapping table (no staged
        // raw text to key on), and must not mislearn against staged line 0.
        var (job, documentId) = await SeedJobWithLinesAsync((0, "STAGED MERCHANT"));
        var payee = await SeedPayeeAsync("Manual Payee");
        var (sut, flows) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                new CommitLineItem(null, FlowDirection.Expense, 25m, new DateOnly(2026, 6, 10), "Cash tip", payee, Learn: true),
            ],
        };

        var result = await sut.Handle(command, TestContext.Current.CancellationToken);

        // The manual line still became a flow.
        Assert.Equal(1, result.FlowsCreated);
        await flows.Received(1).CreateAsync(
            Arg.Is<CreateFlowRequest>(r => r.PayeeId == payee && r.Amount == 25m), Arg.Any<CancellationToken>());

        // …but nothing was learned — a manual line carries no staged origin.
        await _payeeMappings.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_StrictSubsetOfStagedLines_CreatesExactlyThatManyFlows()
    {
        // Three lines staged; the reviewer excludes the middle one, committing only indexes 0 and 2. Excluded
        // rows are simply not sent — the handler creates exactly the flows it receives, no coverage check.
        var (job, documentId) = await SeedJobWithLinesAsync((0, "KEEP A"), (1, "DROP"), (2, "KEEP B"));
        var (sut, _) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "KEEP A"),
                new CommitLineItem(2, FlowDirection.Income, 30m, new DateOnly(2026, 6, 5), "KEEP B"),
            ],
        };

        var result = await sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.FlowsCreated);
        var links = await _db.TransactionDocumentLinks
            .Where(l => l.DocumentId == documentId
                     && l.Origin == TransactionDocumentLinkOrigin.StatementImport)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task Commit_RejectedBalanceEntry_StillCommitsLines()
    {
        var (job, documentId) = await SeedJobAsync();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems = [new CommitLineItem(0, FlowDirection.Expense, 15m, new DateOnly(2026, 6, 3), "COFFEE")],
        };

        var result = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.Committed);   // reviewer declined the assertion
        Assert.Null(result.EntryId);
        Assert.Equal(1, result.FlowsCreated);

        var link = Assert.Single(await _db.TransactionDocumentLinks
            .Where(l => l.DocumentId == documentId)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(TransactionDocumentLinkOrigin.StatementImport, link.Origin);
        Assert.IsType<NormalTransaction>(await _db.AccountTransactions
            .SingleAsync(t => t.Id == link.AccountTransactionId, TestContext.Current.CancellationToken));
        Assert.False(await _db.AccountTransactions.OfType<BalanceEntryTransaction>()
            .AnyAsync(TestContext.Current.CancellationToken));

        await _reconciler.Received(1).ReconcileAsync(
            Arg.Any<IEnumerable<TouchedPair>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_NotAccepted_CreatesNothing()
    {
        var (job, _) = await SeedJobAsync();

        var result = await _sut.Handle(Command(job.Id, accepted: false), TestContext.Current.CancellationToken);

        Assert.False(result.Committed);
        Assert.Null(result.EntryId);
        Assert.False(await _db.AccountTransactions.AnyAsync());
        await _reconciler.DidNotReceive().ReconcileAsync(
            Arg.Any<IEnumerable<TouchedPair>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_AlreadyImportedDocument_Throws()
    {
        var (job, documentId) = await SeedJobAsync();
        // A prior transaction already carries an import-origin link to this document → rejected.
        await SeedLinkedTransactionAsync(documentId, TransactionDocumentLinkOrigin.StatementImport);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_ManualLinkToDocument_DoesNotBlock()
    {
        var (job, documentId) = await SeedJobAsync();
        // A *manual* link to the statement's document must never trip the origin-scoped guard.
        await SeedLinkedTransactionAsync(documentId, TransactionDocumentLinkOrigin.Manual);

        var result = await _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken);

        Assert.True(result.Committed);
        Assert.Single(await _db.TransactionDocumentLinks
            .Where(l => l.DocumentId == documentId
                     && l.Origin == TransactionDocumentLinkOrigin.StatementImport)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_ForeignOwner_ThrowsNotFound()
    {
        var (job, _) = await SeedJobAsync(owner: Guid.NewGuid());

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken));
    }

    // ─── Learning + auto-category (Phase 2) ─────────────────────────────────────
    // These use a SPY IFlowService so the handler's category decision and its learning upserts can be
    // asserted directly (the real FlowService/resolver would obscure the CategoryId the handler passes).

    private (CommitStatementImportHandler sut, IFlowService flows) BuildWithSpyFlows()
    {
        var flows = Substitute.For<IFlowService>();
        flows.CreateAsync(Arg.Any<CreateFlowRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FlowMutationResult(
                new NormalTransaction
                {
                    Id = Guid.NewGuid(),
                    Type = TransactionType.Normal,
                    NormalShape = NormalShape.Expense,
                    OccurredAt = DateTimeOffset.UtcNow,
                    Memo = "spy",
                },
                Array.Empty<TouchedPair>()));
        var sut = new CommitStatementImportHandler(
            _db, _currentUser, _balanceEntries, flows, _reconciler, _payeeMappings, _unitOfWork);
        return (sut, flows);
    }

    private async Task<(ProcessingJob job, Guid documentId)> SeedJobWithLinesAsync(
        params (int index, string? desc)[] lines)
    {
        var documentId = Guid.NewGuid();
        var proposal = new StatementMappingProposal
        {
            AccountNumberMatch = AccountNumberMatch.Unknown,
            LineItems = lines
                .Select(l => new ProposedFlow(
                    l.index, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), l.desc, null, Mappable: true))
                .ToList(),
        };
        var staged = new StagedStatementImport
        {
            AccountId = _account.Id,
            DocumentId = documentId,
            DocumentVersionId = Guid.NewGuid(),
            Ir = new StatementIr { Kind = StatementKind.Bank, Header = new StatementHeaderIr() },
            Proposal = proposal,
        };
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ParseAccountStatement,
            Status = ProcessingJobStatus.Completed,
            CreatedByUserId = _userId,
        };
        job.SetResult(staged);
        _db.ProcessingJobs.Add(job);
        await _db.SaveChangesAsync();
        return (job, documentId);
    }

    private async Task<Guid> SeedPayeeAsync(string name, Guid? defaultCategoryId = null)
    {
        var payee = new Payee { Id = Guid.NewGuid(), Name = name, DefaultCategoryId = defaultCategoryId };
        _db.Payees.Add(payee);
        await _db.SaveChangesAsync();
        return payee.Id;
    }

    private async Task<Account> SeedCategoryAsync(string name, AccountType type, bool isArchived = false)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(), Name = name, AccountType = type,
            ReportingCurrencyId = _cad.Id, IsArchived = isArchived,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Commit_LearnsPayee_OnlyForExplicitLearnLines_KeyedOnStagedRaw_NotEditedMemo()
    {
        var (job, documentId) = await SeedJobWithLinesAsync((0, "SQ *BLUE BOTTLE"), (1, "WALMART"));
        var payeeA = await SeedPayeeAsync("Blue Bottle");
        var payeeB = await SeedPayeeAsync("Walmart");
        var (sut, _) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                // Learn=true → taught; memo edited to prove the key comes from the STAGED raw, not the memo.
                new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "edited memo", payeeA, Learn: true),
                // Learn=false → applied but not taught.
                new CommitLineItem(1, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "WALMART", payeeB, Learn: false),
            ],
        };

        await sut.Handle(command, TestContext.Current.CancellationToken);

        var expectedKey = PayeeDescriptionNormalizer.Normalize("SQ *BLUE BOTTLE")!;
        await _payeeMappings.Received(1).UpsertAsync(
            expectedKey, payeeA, "SQ *BLUE BOTTLE", documentId, Arg.Any<CancellationToken>());
        await _payeeMappings.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), payeeB, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_DoesNotLearn_WhenPayeeNull_OrKeyDegenerate()
    {
        // "A" normalizes to null (below the min-alphabetic floor); a null payee can't learn either.
        var (job, _) = await SeedJobWithLinesAsync((0, "A"), (1, "REAL MERCHANT"));
        var (sut, _) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "A", await SeedPayeeAsync("P"), Learn: true),
                new CommitLineItem(1, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "REAL MERCHANT", null, Learn: true),
            ],
        };

        await sut.Handle(command, TestContext.Current.CancellationToken);

        await _payeeMappings.DidNotReceive().UpsertAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_DeDupesLearningWithinBatch_LastWinsPerKey()
    {
        // "SQ *BLUE BOTTLE" and "TST*BLUE BOTTLE" both normalize to "BLUE BOTTLE".
        var (job, documentId) = await SeedJobWithLinesAsync((0, "SQ *BLUE BOTTLE"), (1, "TST*BLUE BOTTLE"));
        var payeeA = await SeedPayeeAsync("Blue Bottle A");
        var payeeB = await SeedPayeeAsync("Blue Bottle B");
        var (sut, _) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), null, payeeA, Learn: true),
                new CommitLineItem(1, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), null, payeeB, Learn: true),
            ],
        };

        await sut.Handle(command, TestContext.Current.CancellationToken);

        // One upsert for the shared key; last-wins → payeeB.
        await _payeeMappings.Received(1).UpsertAsync(
            "BLUE BOTTLE", Arg.Any<Guid>(), Arg.Any<string>(), documentId, Arg.Any<CancellationToken>());
        await _payeeMappings.Received(1).UpsertAsync(
            "BLUE BOTTLE", payeeB, Arg.Any<string>(), documentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_AutoCategorises_FromPayeeDefault_WhenDirectionMatches()
    {
        var category = await SeedCategoryAsync("Groceries", AccountType.Expense);
        var payee = await SeedPayeeAsync("Grocer", category.Id);
        var (job, _) = await SeedJobWithLinesAsync((0, "GROCER"));
        var (sut, flows) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems = [new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "GROCER", payee)],
        };

        await sut.Handle(command, TestContext.Current.CancellationToken);

        await flows.Received(1).CreateAsync(
            Arg.Is<CreateFlowRequest>(r => r.CategoryId == category.Id && r.PayeeId == payee),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_AutoCategory_FallsBackToUncategorised_OnArchivedOrDirectionMismatch()
    {
        var archived = await SeedCategoryAsync("Old", AccountType.Expense, isArchived: true);
        var incomeCat = await SeedCategoryAsync("Salary", AccountType.Income);
        var payeeArchived = await SeedPayeeAsync("P1", archived.Id);
        var payeeMismatch = await SeedPayeeAsync("P2", incomeCat.Id); // Income category on an Expense line
        var (job, _) = await SeedJobWithLinesAsync((0, "A"), (1, "B"));
        var (sut, flows) = BuildWithSpyFlows();

        var command = Command(job.Id, accepted: false) with
        {
            LineItems =
            [
                new CommitLineItem(0, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "A", payeeArchived),
                new CommitLineItem(1, FlowDirection.Expense, 10m, new DateOnly(2026, 6, 3), "B", payeeMismatch),
            ],
        };

        // Never throws — both archived and wrong-direction categories degrade to Uncategorised (null).
        await sut.Handle(command, TestContext.Current.CancellationToken);

        await flows.Received(2).CreateAsync(
            Arg.Is<CreateFlowRequest>(r => r.CategoryId == null), Arg.Any<CancellationToken>());
    }
}
