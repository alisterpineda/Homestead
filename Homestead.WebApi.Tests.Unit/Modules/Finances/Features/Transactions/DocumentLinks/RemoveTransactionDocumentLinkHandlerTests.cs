using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.RemoveTransactionDocumentLink;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Transactions.DocumentLinks;

public class RemoveTransactionDocumentLinkHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private RemoveTransactionDocumentLinkHandler _sut = null!;

    private BalanceEntryTransaction _transaction = null!;

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
        await _db.SaveChangesAsync();

        _audit = Substitute.For<IAuditContext>();

        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _sut = new RemoveTransactionDocumentLinkHandler(_db, _audit, _unitOfWork);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> SeedLinkAsync(TransactionDocumentLinkOrigin origin)
    {
        var documentId = Guid.NewGuid();
        _db.TransactionDocumentLinks.Add(new TransactionDocumentLink
        {
            AccountTransactionId = _transaction.Id,
            DocumentId = documentId,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return documentId;
    }

    [Fact]
    public async Task Remove_NoSuchLink_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(_transaction.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_ImportOriginLink_Throws_AndRowSurvives()
    {
        var documentId = await SeedLinkAsync(TransactionDocumentLinkOrigin.StatementImport);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.HandleAsync(_transaction.Id, documentId, TestContext.Current.CancellationToken));

        Assert.True(await _db.TransactionDocumentLinks.AnyAsync(
            l => l.AccountTransactionId == _transaction.Id && l.DocumentId == documentId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Remove_ManualLink_DeletesRow_AndAudits()
    {
        var documentId = await SeedLinkAsync(TransactionDocumentLinkOrigin.Manual);

        await _sut.HandleAsync(_transaction.Id, documentId, TestContext.Current.CancellationToken);

        Assert.False(await _db.TransactionDocumentLinks.AnyAsync(
            l => l.AccountTransactionId == _transaction.Id && l.DocumentId == documentId,
            TestContext.Current.CancellationToken));

        _audit.Received(1).Deleted(
            "TransactionDocumentLink",
            TransactionDocumentLink.SyntheticId(_transaction.Id, documentId),
            null,
            _transaction.Id);
    }
}
