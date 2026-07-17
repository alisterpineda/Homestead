using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.Imports.InitiateStatementImport;
using Homestead.WebApi.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Imports;

public class InitiateStatementImportHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ICurrentUserAccessor _currentUser = null!;
    private IDocumentQueryService _documentQuery = null!;
    private IJobQueue _jobQueue = null!;
    private InitiateStatementImportHandler _sut = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private Commodity _cad = null!;
    private Account _account = null!;
    private Document _document = null!;
    private DocumentVersion _version = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);

        _account = new Account
        {
            Id = Guid.NewGuid(), Name = "Chequing", AccountType = AccountType.Asset, Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id, IsSystem = false, IsArchived = false,
        };
        _db.Accounts.Add(_account);

        _document = new Document { Id = Guid.NewGuid(), Name = "June statement" };
        _version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _document.Id,
            VersionNumber = 1,
            ContentType = "application/pdf",
            FilePath = "documents/files/abcd/abcd.pdf",
            FileHash = "abcd1234",
            FileExtension = ".pdf",
            ExtractedText = "STATEMENT OF ACCOUNT — closing balance 1,500.00",
        };
        _db.Documents.Add(_document);
        _db.DocumentVersions.Add(_version);
        await _db.SaveChangesAsync();

        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);

        _documentQuery = Substitute.For<IDocumentQueryService>();
        _documentQuery.AccessibleDocuments()
            .Returns(_ => _db.Documents);

        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(
                Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        _sut = new InitiateStatementImportHandler(
            _db, _currentUser, _documentQuery, _jobQueue,
            NullLogger<InitiateStatementImportHandler>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SeedLinkedTransactionAsync(TransactionDocumentLinkOrigin origin)
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
            DocumentId = _document.Id,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private InitiateStatementImportCommand Command() => new()
    {
        AccountId = _account.Id,
        DocumentVersionId = _version.Id,
    };

    [Fact]
    public async Task Initiate_AlreadyImportedDocument_Throws_AndEnqueuesNothing()
    {
        await SeedLinkedTransactionAsync(TransactionDocumentLinkOrigin.StatementImport);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(Command(), TestContext.Current.CancellationToken));

        await _jobQueue.DidNotReceive().EnqueueAsync(
            Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initiate_ManualLinkToDocument_DoesNotBlock()
    {
        // A *manual* link (receipt attached by hand) must never trip the origin-scoped dedup guard.
        await SeedLinkedTransactionAsync(TransactionDocumentLinkOrigin.Manual);

        var result = await _sut.Handle(Command(), TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.JobId);
        await _jobQueue.Received(1).EnqueueAsync(
            ProcessingJobType.ParseAccountStatement,
            Arg.Any<Action<EnqueueJobRequest>?>(),
            Arg.Any<CancellationToken>());
    }
}
