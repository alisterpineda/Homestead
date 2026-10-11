using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;
using Homestead.WebApi.Modules.Finances.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Imports;

public class RollbackStatementImportHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IBalanceEntryService _balanceEntries = null!;
    private ITransferService _transfers = null!;
    private IBalanceEntryReconciler _reconciler = null!;
    private IPayeeMappingService _payeeMappings = null!;
    private IUnitOfWork _unitOfWork = null!;
    private RollbackStatementImportHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();

        _balanceEntries = Substitute.For<IBalanceEntryService>();
        _balanceEntries.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BalanceEntryMutationResult(
                null, [new TouchedPair(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow)]));

        _transfers = Substitute.For<ITransferService>();
        _transfers.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new TransferMutationResult(
                null, [new TouchedPair(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow)]));

        _reconciler = Substitute.For<IBalanceEntryReconciler>();

        _payeeMappings = Substitute.For<IPayeeMappingService>();
        _payeeMappings.RetractByDocumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(0);

        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _sut = new RollbackStatementImportHandler(
            _db, _balanceEntries, _transfers, _reconciler, _payeeMappings, _unitOfWork);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> SeedLinkedTransactionAsync(
        Guid documentId, TransactionType type, TransactionDocumentLinkOrigin origin)
    {
        AccountTransaction transaction = type == TransactionType.BalanceEntry
            ? new BalanceEntryTransaction
            {
                Id = Guid.NewGuid(), OccurredAt = new DateOnly(2026, 6, 30).ToEndOfDayUtc(), Memo = "entry",
                Type = TransactionType.BalanceEntry, AccountId = Guid.NewGuid(),
                AssertedCommodityId = Guid.NewGuid(), AssertedQuantity = 100m,
            }
            : new NormalTransaction
            {
                Id = Guid.NewGuid(), OccurredAt = new DateOnly(2026, 6, 15).ToEndOfDayUtc(), Memo = "flow",
                Type = TransactionType.Normal,
            };
        _db.AccountTransactions.Add(transaction);
        _db.TransactionDocumentLinks.Add(new TransactionDocumentLink
        {
            AccountTransactionId = transaction.Id,
            DocumentId = documentId,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return transaction.Id;
    }

    [Fact]
    public async Task Rollback_DispatchesByType_SkipsManualLinks_AndReconcilesOnce()
    {
        var documentId = Guid.NewGuid();
        var balanceEntryId = await SeedLinkedTransactionAsync(
            documentId, TransactionType.BalanceEntry, TransactionDocumentLinkOrigin.StatementImport);
        var normalId = await SeedLinkedTransactionAsync(
            documentId, TransactionType.Normal, TransactionDocumentLinkOrigin.StatementImport);
        var manualId = await SeedLinkedTransactionAsync(
            documentId, TransactionType.Normal, TransactionDocumentLinkOrigin.Manual);

        var response = await _sut.HandleAsync(
            documentId,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, response.DeletedCount);

        await _balanceEntries.Received(1).DeleteAsync(balanceEntryId, Arg.Any<CancellationToken>());
        await _transfers.Received(1).DeleteAsync(normalId, Arg.Any<CancellationToken>());

        // The manually-linked transaction is outside the rollback scope — never dispatched.
        await _balanceEntries.DidNotReceive().DeleteAsync(manualId, Arg.Any<CancellationToken>());
        await _transfers.DidNotReceive().DeleteAsync(manualId, Arg.Any<CancellationToken>());

        await _reconciler.Received(1).ReconcileAsync(
            Arg.Any<IEnumerable<TouchedPair>>(), Arg.Any<CancellationToken>());

        // Learning taught by this import is retracted, scoped to the same document.
        await _payeeMappings.Received(1).RetractByDocumentAsync(documentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rollback_NoImportOriginLinks_ThrowsNotFound()
    {
        var documentId = Guid.NewGuid();
        // A manual link alone must not make the document "imported".
        await SeedLinkedTransactionAsync(
            documentId, TransactionType.Normal, TransactionDocumentLinkOrigin.Manual);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.HandleAsync(
                documentId,
                TestContext.Current.CancellationToken));

        await _balanceEntries.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _transfers.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
