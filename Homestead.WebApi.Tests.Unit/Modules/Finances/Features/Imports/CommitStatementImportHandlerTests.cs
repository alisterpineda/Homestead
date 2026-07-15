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
    private BalanceEntryService _balanceEntries = null!;
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
        _account = new Account
        {
            Id = Guid.NewGuid(), Name = "Chequing", AccountType = AccountType.Asset, Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id, IsSystem = false, IsArchived = false,
        };
        _db.Accounts.AddRange(adjustments, _account);
        await _db.SaveChangesAsync();

        var systemAccounts = Substitute.For<ISystemAccountProvider>();
        systemAccounts.GetAdjustmentsAsync(Arg.Any<CancellationToken>()).Returns(adjustments);

        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);

        _reconciler = Substitute.For<IBalanceEntryReconciler>();

        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _balanceEntries = new BalanceEntryService(
            _db, systemAccounts, new TransactionValidator(_db), Substitute.For<IAuditContext>());

        _sut = new CommitStatementImportHandler(_db, _currentUser, _balanceEntries, _reconciler, _unitOfWork);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<(ProcessingJob job, Guid versionId)> SeedJobAsync(Guid? owner = null)
    {
        var versionId = Guid.NewGuid();
        var staged = new StagedStatementImport
        {
            AccountId = _account.Id,
            DocumentId = Guid.NewGuid(),
            DocumentVersionId = versionId,
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
        return (job, versionId);
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
    public async Task Commit_Accepted_CreatesEntry_StampsSource_AndReconciles()
    {
        var (job, versionId) = await SeedJobAsync();

        var result = await _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken);

        Assert.True(result.Committed);
        Assert.NotNull(result.EntryId);
        Assert.Equal(_account.Id, result.AccountId);

        var entry = await _db.AccountTransactions.OfType<BalanceEntryTransaction>()
            .SingleAsync(e => e.Id == result.EntryId);
        Assert.Equal(versionId, entry.SourceDocumentVersionId);
        Assert.Equal(_account.Id, entry.AccountId);
        Assert.Equal(_cad.Id, entry.AssertedCommodityId);

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
    public async Task Commit_AlreadyImportedVersion_Throws()
    {
        var (job, versionId) = await SeedJobAsync();
        // A prior transaction already carries this source version → re-import must be rejected.
        _db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            Id = Guid.NewGuid(), OccurredAt = new DateOnly(2026, 5, 31).ToEndOfDayUtc(), Memo = "prior",
            Type = TransactionType.BalanceEntry, AccountId = _account.Id, AssertedCommodityId = _cad.Id,
            AssertedQuantity = 1m, SourceDocumentVersionId = versionId,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_ForeignOwner_ThrowsNotFound()
    {
        var (job, _) = await SeedJobAsync(owner: Guid.NewGuid());

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(Command(job.Id), TestContext.Current.CancellationToken));
    }
}
