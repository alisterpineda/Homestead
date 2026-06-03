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
}
