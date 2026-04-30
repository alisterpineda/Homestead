using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class LinkedPriceManagerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private LinkedPriceManager _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;
    private Commodity _veqt = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Exchange = "TSX", Name = "VEQT", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_cad, _usd, _veqt);
        await _db.SaveChangesAsync();

        _audit = Substitute.For<IAuditContext>();
        _sut = new LinkedPriceManager(_db, _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private NormalTransaction TxnWithSplits(DateTimeOffset at, params AccountTransactionSplit[] splits)
    {
        var txn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = at,
            Memo = "Test",
            Type = TransactionType.Normal,
            Splits = splits.ToList(),
        };
        _db.AccountTransactions.Add(txn);
        return txn;
    }

    private static AccountTransactionSplit Split(Guid commodity, decimal qty, decimal? rate = null, Guid? quote = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            CommodityId = commodity,
            Quantity = qty,
            UnitPriceRate = rate,
            UnitPriceCommodityId = quote,
        };

    [Fact]
    public async Task SyncForTransaction_WithAnnotatedSplit_CreatesLinkedRow()
    {
        var at = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var txn = TxnWithSplits(at,
            Split(_usd.Id, -100m, 1.35m, _cad.Id),
            Split(_cad.Id, 135m));

        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_usd.Id, price.CommodityId);
        Assert.Equal(_cad.Id, price.QuoteCurrencyId);
        Assert.Equal(1.35m, price.Rate);
        Assert.Equal(at, price.PriceAt);
        Assert.Equal(CommodityPriceSource.Transaction, price.Source);
        Assert.Equal(txn.Id, price.CreatedByTransactionId);

        _audit.Received(1).Created("CommodityPrice", Arg.Any<Guid>());
    }

    [Fact]
    public async Task SyncForTransaction_WithoutAnnotation_CreatesNoRow()
    {
        var at = new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero);
        var txn = TxnWithSplits(at, Split(_cad.Id, 100m), Split(_cad.Id, -100m));

        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.CommodityPrices.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncForTransaction_RateChange_UpdatesRowInPlace()
    {
        var at = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var txn = TxnWithSplits(at,
            Split(_veqt.Id, 100m, 30m, _cad.Id),
            Split(_cad.Id, -3000m));
        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rowId = (await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken)).Id;

        // Change the rate on the annotated split.
        var annotated = txn.Splits.First(s => s.UnitPriceRate is not null);
        annotated.UnitPriceRate = 40m;

        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prices = await _db.CommodityPrices.ToListAsync(TestContext.Current.CancellationToken);
        var price = Assert.Single(prices);
        Assert.Equal(rowId, price.Id);
        Assert.Equal(40m, price.Rate);
    }

    [Fact]
    public async Task SyncForTransaction_AnnotationRemoved_DeletesRow()
    {
        var at = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var txn = TxnWithSplits(at,
            Split(_veqt.Id, 100m, 30m, _cad.Id),
            Split(_cad.Id, -3000m));
        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Remove the annotation.
        var annotated = txn.Splits.First(s => s.UnitPriceRate is not null);
        annotated.UnitPriceRate = null;
        annotated.UnitPriceCommodityId = null;

        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await _db.CommodityPrices.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncForTransaction_CollidingTransactionRow_Throws()
    {
        var at = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceAt = at,
            Rate = 99m,
            Source = CommodityPriceSource.Transaction,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = TxnWithSplits(at,
            Split(_veqt.Id, 100m, 30m, _cad.Id),
            Split(_cad.Id, -3000m));

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncForTransaction_ManualRowAtSameInstant_Coexists()
    {
        var at = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var manualId = Guid.NewGuid();
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = manualId,
            CommodityId = _veqt.Id,
            QuoteCurrencyId = _cad.Id,
            PriceAt = at,
            Rate = 99m,
            Source = CommodityPriceSource.Manual,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = TxnWithSplits(at,
            Split(_veqt.Id, 100m, 30m, _cad.Id),
            Split(_cad.Id, -3000m));

        await _sut.SyncForTransactionAsync(txn, TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var prices = await _db.CommodityPrices
            .Where(p => p.CommodityId == _veqt.Id && p.QuoteCurrencyId == _cad.Id && p.PriceAt == at)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, prices.Count);
        Assert.Contains(prices, p => p.Id == manualId && p.Source == CommodityPriceSource.Manual);
        Assert.Contains(prices, p => p.Source == CommodityPriceSource.Transaction
                                  && p.CreatedByTransactionId == txn.Id);
    }
}
