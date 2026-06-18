using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class TransferServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private TransferService _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.AddRange(_cad, _usd);
        await _db.SaveChangesAsync();

        _audit = Substitute.For<IAuditContext>();
        var validator = new TransactionValidator(_db);
        var linkedPrices = new LinkedPriceManager(_db, _audit);
        _sut = new TransferService(_db, validator, linkedPrices, _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddAccount(AccountKind kind, Guid currencyId, string name)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = kind.ToAccountType(),
            Kind = kind,
            ReportingCurrencyId = currencyId,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Create_SameCurrencyTransfer_BalancesPerCommodity_NoLinkedPrice()
    {
        var chequing = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");
        var savings = await AddAccount(AccountKind.Cash, _cad.Id, "Savings");

        var result = await _sut.CreateAsync(
            new CreateTransferRequest(chequing.Id, savings.Id, 200m, null, new DateOnly(2026, 4, 23), null),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = result.Transaction!;
        var sourceSplit = txn.Splits.Single(s => s.AccountId == chequing.Id);
        var destSplit = txn.Splits.Single(s => s.AccountId == savings.Id);

        Assert.Equal(-200m, sourceSplit.Quantity);
        Assert.Equal(_cad.Id, sourceSplit.CommodityId);
        Assert.Null(sourceSplit.UnitPriceRate);
        Assert.Equal(200m, destSplit.Quantity);
        Assert.Equal(_cad.Id, destSplit.CommodityId);

        Assert.Equal(0, await _db.CommodityPrices.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero), txn.OccurredAt);
    }

    [Fact]
    public async Task Create_CrossCurrencyTransfer_AnnotatesSourceLeg_EmitsLinkedPrice()
    {
        var usdChequing = await AddAccount(AccountKind.Cash, _usd.Id, "USD Chequing");
        var cadChequing = await AddAccount(AccountKind.Cash, _cad.Id, "CAD Chequing");

        var result = await _sut.CreateAsync(
            new CreateTransferRequest(usdChequing.Id, cadChequing.Id, 100m, 135m, new DateOnly(2026, 4, 23), null),
            TestContext.Current.CancellationToken);

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sourceSplit = result.Transaction!.Splits.Single(s => s.AccountId == usdChequing.Id);
        var destSplit = result.Transaction!.Splits.Single(s => s.AccountId == cadChequing.Id);

        Assert.Equal(-100m, sourceSplit.Quantity);
        Assert.Equal(_usd.Id, sourceSplit.CommodityId);
        Assert.Equal(1.35m, sourceSplit.UnitPriceRate);
        Assert.Equal(_cad.Id, sourceSplit.UnitPriceCommodityId);

        Assert.Equal(135m, destSplit.Quantity);
        Assert.Equal(_cad.Id, destSplit.CommodityId);
        Assert.Null(destSplit.UnitPriceRate);

        var price = await _db.CommodityPrices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_usd.Id, price.CommodityId);
        Assert.Equal(_cad.Id, price.QuoteCurrencyId);
        Assert.Equal(1.35m, price.Rate);
    }

    [Fact]
    public async Task Create_PayDownCreditCardFromChequing_BalancesPerCommodity()
    {
        var creditCard = await AddAccount(AccountKind.CreditCard, _cad.Id, "CC");
        var chequing = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        // Pay $50 from chequing to credit card (paying down the card).
        var result = await _sut.CreateAsync(
            new CreateTransferRequest(chequing.Id, creditCard.Id, 50m, null, new DateOnly(2026, 4, 23), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sourceSplit = result.Transaction!.Splits.Single(s => s.AccountId == chequing.Id);
        var ccSplit = result.Transaction!.Splits.Single(s => s.AccountId == creditCard.Id);

        // Source (chequing/asset) -50: balance decreases.
        // Destination (CC/liability) +50: stored balance becomes "less negative" — owed amount drops.
        // The transaction balances: -50 + 50 = 0 in CAD.
        Assert.Equal(-50m, sourceSplit.Quantity);
        Assert.Equal(50m, ccSplit.Quantity);
    }

    [Fact]
    public async Task Create_SameAccount_Throws()
    {
        var chequing = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTransferRequest(chequing.Id, chequing.Id, 100m, null, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Throws()
    {
        var chequing = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");
        var savings = await AddAccount(AccountKind.Cash, _cad.Id, "Savings");
        chequing.IsArchived = true;
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTransferRequest(chequing.Id, savings.Id, 100m, null, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_CrossCurrencyMissingDestinationAmount_Throws()
    {
        var usdChequing = await AddAccount(AccountKind.Cash, _usd.Id, "USD");
        var cadChequing = await AddAccount(AccountKind.Cash, _cad.Id, "CAD");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTransferRequest(usdChequing.Id, cadChequing.Id, 100m, null, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NegativeAmount_Throws()
    {
        var a = await AddAccount(AccountKind.Cash, _cad.Id, "A");
        var b = await AddAccount(AccountKind.Cash, _cad.Id, "B");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateTransferRequest(a.Id, b.Id, -100m, null, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }

    // These ratios leave a residue when the source is multiplied back through the rate (verified:
    // sourceAmount * (dest/source) != dest in decimal), which is exactly what tripped the old code.
    [Theory]
    [InlineData(3, 1)]  // rate 1/3   → 3 * (1/3)  = 0.999…9 ≠ 1
    [InlineData(3, 2)]  // rate 2/3   → 3 * (2/3)  = 2.000…1 ≠ 2
    [InlineData(6, 7)]  // rate 7/6   → 6 * (7/6)  = 7.000…2 ≠ 7
    public async Task Create_CrossCurrencyTransfer_NonTerminatingRate_BothLegsBalanceExactly(
        int sourceAmountInt, int destinationAmountInt)
    {
        // A non-terminating FX ratio used to leave a ~1e-28 residue: the source leg was annotated with
        // rate = dest/source while the destination leg stored the raw typed amount, so the validator's
        // recomputed source weight (Quantity * rate) didn't recover the destination exactly and the
        // zero-tolerance balance check threw 400. Deriving the destination from sourceAmount * rate
        // makes both legs cancel exactly. Mirrors DripServiceTests' non-terminating-price test.
        var usd = await AddAccount(AccountKind.Cash, _usd.Id, "USD");
        var cad = await AddAccount(AccountKind.Cash, _cad.Id, "CAD");

        decimal sourceAmount = sourceAmountInt;
        decimal destinationAmount = destinationAmountInt;
        var rate = destinationAmount / sourceAmount;
        Assert.NotEqual(destinationAmount, sourceAmount * rate);  // guard: this case is non-terminating

        // CreateAsync runs the validator internally — not throwing here is the regression assertion.
        var result = await _sut.CreateAsync(
            new CreateTransferRequest(usd.Id, cad.Id, sourceAmount, destinationAmount,
                new DateOnly(2026, 4, 23), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sourceSplit = result.Transaction!.Splits.Single(s => s.AccountId == usd.Id);
        var destSplit = result.Transaction!.Splits.Single(s => s.AccountId == cad.Id);

        Assert.Equal(-sourceAmount, sourceSplit.Quantity);
        Assert.Equal(rate, sourceSplit.UnitPriceRate);
        Assert.Equal(_cad.Id, sourceSplit.UnitPriceCommodityId);

        // Destination quantity is derived from the same product as the source weight.
        Assert.Equal(sourceAmount * rate, destSplit.Quantity);
        Assert.Null(destSplit.UnitPriceRate);

        // The two legs cancel to exactly zero in the destination (annotation) commodity.
        Assert.Equal(0m, sourceSplit.Quantity * sourceSplit.UnitPriceRate!.Value + destSplit.Quantity);
    }

    [Fact]
    public async Task Update_ToCrossCurrencyNonTerminatingRate_BothLegsBalanceExactly()
    {
        var usd = await AddAccount(AccountKind.Cash, _usd.Id, "USD");
        var cad = await AddAccount(AccountKind.Cash, _cad.Id, "CAD");

        // Start from a terminating-rate transfer, then edit it to a non-terminating ratio so the edit
        // path exercises the same bridge arithmetic as create (and can't drift from it).
        var created = await _sut.CreateAsync(
            new CreateTransferRequest(usd.Id, cad.Id, 100m, 135m, new DateOnly(2026, 4, 23), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sourceAmount = 3m;
        var destinationAmount = 10m;
        var rate = destinationAmount / sourceAmount;

        var result = await _sut.UpdateAsync(created.Transaction!.Id,
            new UpdateTransferRequest(usd.Id, cad.Id, sourceAmount, destinationAmount,
                new DateOnly(2026, 4, 24), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sourceSplit = result.Transaction!.Splits.Single(s => s.AccountId == usd.Id);
        var destSplit = result.Transaction!.Splits.Single(s => s.AccountId == cad.Id);

        Assert.Equal(-sourceAmount, sourceSplit.Quantity);
        Assert.Equal(rate, sourceSplit.UnitPriceRate);
        Assert.Equal(_cad.Id, sourceSplit.UnitPriceCommodityId);
        Assert.Equal(sourceAmount * rate, destSplit.Quantity);
        Assert.Null(destSplit.UnitPriceRate);
        Assert.Equal(0m, sourceSplit.Quantity * sourceSplit.UnitPriceRate!.Value + destSplit.Quantity);
    }

    [Fact]
    public async Task Update_OnNonTransferNormal_Throws_ShapeIsImmutable()
    {
        var a = await AddAccount(AccountKind.Cash, _cad.Id, "A");
        var b = await AddAccount(AccountKind.Cash, _cad.Id, "B");

        // A 2-split normal that is NOT a transfer (e.g. an investment buy). The transfer editor must
        // refuse it so it can't rewrite the splits while leaving NormalShape=InvestmentBuy.
        var trade = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero),
            Memo = "Buy",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.InvestmentBuy,
            Splits =
            [
                new() { Id = Guid.NewGuid(), AccountId = a.Id, CommodityId = _cad.Id, Quantity = -100m },
                new() { Id = Guid.NewGuid(), AccountId = a.Id, CommodityId = _cad.Id, Quantity = 100m },
            ],
        };
        _db.AccountTransactions.Add(trade);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(trade.Id,
                new UpdateTransferRequest(a.Id, b.Id, 50m, null, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));

        // The row is untouched: still an InvestmentBuy with both legs on account A.
        var reloaded = await _db.AccountTransactions
            .OfType<NormalTransaction>()
            .Include(t => t.Splits)
            .SingleAsync(t => t.Id == trade.Id, TestContext.Current.CancellationToken);
        Assert.Equal(NormalShape.InvestmentBuy, reloaded.NormalShape);
        Assert.All(reloaded.Splits, s => Assert.Equal(a.Id, s.AccountId));
    }
}
