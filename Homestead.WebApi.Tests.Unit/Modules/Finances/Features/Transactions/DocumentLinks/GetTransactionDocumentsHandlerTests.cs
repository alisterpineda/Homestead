using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks;
using Homestead.WebApi.Modules.Finances.Features.Transactions.DocumentLinks.GetTransactionDocuments;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Transactions.DocumentLinks;

public class GetTransactionDocumentsHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IDocumentQueryService _documentQuery = null!;
    private GetTransactionDocumentsHandler _sut = null!;

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

        _documentQuery = Substitute.For<IDocumentQueryService>();
        _documentQuery.AccessibleDocuments(TrashFilter.IncludeTrashed)
            .Returns(_ => _db.Documents.IgnoreQueryFilters([DocumentQueryFilters.SoftDelete]));

        _sut = new GetTransactionDocumentsHandler(_db, _documentQuery);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Document> SeedDocumentAsync(string name, DateTimeOffset? deletedAt = null)
    {
        var document = new Document { Id = Guid.NewGuid(), Name = name, DeletedAt = deletedAt };
        _db.Documents.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }

    private async Task SeedLinkAsync(Guid documentId, TransactionDocumentLinkOrigin origin, DateTimeOffset createdAt)
    {
        _db.TransactionDocumentLinks.Add(new TransactionDocumentLink
        {
            AccountTransactionId = _transaction.Id,
            DocumentId = documentId,
            Origin = origin,
            CreatedAt = createdAt,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_UnknownTransaction_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_NoLinks_ReturnsEmptyList()
    {
        var result = await _sut.HandleAsync(
            _transaction.Id, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Get_MapsAvailability_LiveTrashedAndFilteredOut()
    {
        var now = DateTimeOffset.UtcNow;
        var live = await SeedDocumentAsync("Receipt");
        // Trashed: only reachable because the handler asks for TrashFilter.IncludeTrashed —
        // under ActiveOnly this link would come back Unavailable.
        var trashed = await SeedDocumentAsync("Old statement", deletedAt: now);
        var inaccessible = await SeedDocumentAsync("Someone else's");

        await SeedLinkAsync(live.Id, TransactionDocumentLinkOrigin.Manual, now.AddMinutes(-3));
        await SeedLinkAsync(trashed.Id, TransactionDocumentLinkOrigin.StatementImport, now.AddMinutes(-2));
        await SeedLinkAsync(inaccessible.Id, TransactionDocumentLinkOrigin.Manual, now.AddMinutes(-1));

        _documentQuery.AccessibleDocuments(TrashFilter.IncludeTrashed)
            .Returns(_ => _db.Documents.IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
                .Where(d => d.Id != inaccessible.Id));

        var result = await _sut.HandleAsync(
            _transaction.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);

        var liveRow = Assert.Single(result, r => r.DocumentId == live.Id);
        Assert.Equal(DocumentLinkAvailability.Available, liveRow.Availability);
        Assert.Equal("Receipt", liveRow.DocumentName);
        Assert.Equal(TransactionDocumentLinkOrigin.Manual, liveRow.Origin);

        var trashedRow = Assert.Single(result, r => r.DocumentId == trashed.Id);
        Assert.Equal(DocumentLinkAvailability.Trashed, trashedRow.Availability);
        Assert.Equal("Old statement", trashedRow.DocumentName);
        Assert.Equal(TransactionDocumentLinkOrigin.StatementImport, trashedRow.Origin);

        var unavailableRow = Assert.Single(result, r => r.DocumentId == inaccessible.Id);
        Assert.Equal(DocumentLinkAvailability.Unavailable, unavailableRow.Availability);
        Assert.Null(unavailableRow.DocumentName);
    }

    [Fact]
    public async Task Get_OrdersByCreatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var newest = await SeedDocumentAsync("Newest");
        var oldest = await SeedDocumentAsync("Oldest");
        var middle = await SeedDocumentAsync("Middle");

        // Inserted out of chronological order — output must follow CreatedAt, not insertion.
        await SeedLinkAsync(newest.Id, TransactionDocumentLinkOrigin.Manual, now.AddMinutes(-1));
        await SeedLinkAsync(oldest.Id, TransactionDocumentLinkOrigin.Manual, now.AddMinutes(-10));
        await SeedLinkAsync(middle.Id, TransactionDocumentLinkOrigin.Manual, now.AddMinutes(-5));

        var result = await _sut.HandleAsync(
            _transaction.Id, TestContext.Current.CancellationToken);

        Assert.Equal([oldest.Id, middle.Id, newest.Id], result.Select(r => r.DocumentId));
    }
}
