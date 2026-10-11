using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.AddTransactionDocumentLink;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Transactions.DocumentLinks;

public class AddTransactionDocumentLinkHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IDocumentQueryService _documentQuery = null!;
    private ICurrentUserAccessor _currentUser = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private AddTransactionDocumentLinkHandler _sut = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private BalanceEntryTransaction _transaction = null!;
    private Document _document = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();

        _transaction = new BalanceEntryTransaction
        {
            Id = Guid.NewGuid(), OccurredAt = new DateOnly(2026, 6, 30).ToEndOfDayUtc(), Memo = "entry",
            Type = TransactionType.BalanceEntry, AccountId = Guid.NewGuid(),
            AssertedCommodityId = Guid.NewGuid(), AssertedQuantity = 100m,
        };
        _db.AccountTransactions.Add(_transaction);

        _document = new Document { Id = Guid.NewGuid(), Name = "Receipt" };
        _db.Documents.Add(_document);
        await _db.SaveChangesAsync();

        _documentQuery = Substitute.For<IDocumentQueryService>();
        _documentQuery.AccessibleDocuments()
            .Returns(_ => _db.Documents);

        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);

        _audit = Substitute.For<IAuditContext>();

        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _sut = new AddTransactionDocumentLinkHandler(_db, _documentQuery, _currentUser, _audit, _unitOfWork);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Add_UnknownTransaction_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                Guid.NewGuid(), new AddTransactionDocumentLinkRequest(_document.Id),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_UnknownDocument_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                _transaction.Id, new AddTransactionDocumentLinkRequest(Guid.NewGuid()),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_TrashedDocument_ThrowsNotFound()
    {
        var trashed = new Document { Id = Guid.NewGuid(), Name = "Trashed", DeletedAt = DateTimeOffset.UtcNow };
        _db.Documents.Add(trashed);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                _transaction.Id, new AddTransactionDocumentLinkRequest(trashed.Id),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_InaccessibleDocument_ThrowsNotFound()
    {
        _documentQuery.AccessibleDocuments()
            .Returns(_ => _db.Documents.Where(d => d.Id != _document.Id));

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                _transaction.Id, new AddTransactionDocumentLinkRequest(_document.Id),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(TransactionDocumentLinkOrigin.Manual)]
    [InlineData(TransactionDocumentLinkOrigin.StatementImport)]
    public async Task Add_ExistingLink_ThrowsDuplicate(TransactionDocumentLinkOrigin existingOrigin)
    {
        _db.TransactionDocumentLinks.Add(new TransactionDocumentLink
        {
            AccountTransactionId = _transaction.Id,
            DocumentId = _document.Id,
            Origin = existingOrigin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DuplicateEntityException>(
            () => _sut.HandleAsync(
                _transaction.Id, new AddTransactionDocumentLinkRequest(_document.Id),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_PersistsManualLink_ReturnsResponse_AndAudits()
    {
        var response = await _sut.HandleAsync(
            _transaction.Id, new AddTransactionDocumentLinkRequest(_document.Id),
            TestContext.Current.CancellationToken);

        var link = await _db.TransactionDocumentLinks.SingleAsync(
            l => l.AccountTransactionId == _transaction.Id && l.DocumentId == _document.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal(TransactionDocumentLinkOrigin.Manual, link.Origin);
        Assert.Equal(_userId, link.CreatedByUserId);

        Assert.Equal(_document.Id, response.DocumentId);
        Assert.Equal("Receipt", response.DocumentName);
        Assert.Equal(TransactionDocumentLinkOrigin.Manual, response.Origin);
        Assert.Equal(DocumentLinkAvailability.Available, response.Availability);

        _audit.Received(1).Created(
            "TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(_transaction.Id, _document.Id),
            _transaction.Id);
    }
}
