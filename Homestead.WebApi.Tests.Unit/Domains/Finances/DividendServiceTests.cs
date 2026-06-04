using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class DividendServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ISystemAccountProvider _systemAccounts = null!;
    private IAuditContext _audit = null!;
    private DividendService _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;
    private Account _income = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.AddRange(_cad, _usd);

        _income = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Income",
            AccountType = AccountType.Income,
            Kind = null,
            ReportingCurrencyId = _cad.Id,
            IsSystem = true,
            IsArchived = false,
        };
        _db.Accounts.Add(_income);
        await _db.SaveChangesAsync();

        _systemAccounts = Substitute.For<ISystemAccountProvider>();
        _systemAccounts.GetIncomeAsync(Arg.Any<CancellationToken>()).Returns(_income);

        _audit = Substitute.For<IAuditContext>();
        _sut = new DividendService(_db, _systemAccounts, new TransactionValidator(_db), _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> AddInvestmentAsync(Guid currencyId, string name = "RRSP")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = AccountKind.Investment.ToAccountType(),
            Kind = AccountKind.Investment,
            ReportingCurrencyId = currencyId,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Create_PostsPositiveAccountLeg_NegativeIncomeLeg_Balances()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        var result = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _cad.Id, 12.50m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.Equal(NormalShape.Dividend, txn.NormalShape);

        var accountLeg = txn.Splits.Single(s => s.AccountId == investment.Id);
        var incomeLeg = txn.Splits.Single(s => s.AccountId == _income.Id);

        Assert.Equal(12.50m, accountLeg.Quantity);
        Assert.Equal(_cad.Id, accountLeg.CommodityId);
        Assert.Equal(-12.50m, incomeLeg.Quantity);
        Assert.Equal(_cad.Id, incomeLeg.CommodityId);

        // No annotation, so no linked price row.
        Assert.Equal(0, await _db.CommodityPrices.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ForeignCurrencyIntoSingleCurrencyAccount_IsAllowed()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        var result = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _usd.Id, 5m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.All(txn.Splits, s => Assert.Equal(_usd.Id, s.CommodityId));
    }

    [Fact]
    public async Task Create_BlankDescription_DefaultsToDividend()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        var result = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _cad.Id, 10m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), Description: "   "),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dividend", result.Transaction!.Memo);
    }

    [Fact]
    public async Task Create_NonBlankDescription_PersistsTrimmed()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        var result = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _cad.Id, 10m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), Description: "  Q1 distribution  "),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Q1 distribution", result.Transaction!.Memo);
    }

    [Fact]
    public async Task Create_NonInvestmentAccount_Throws()
    {
        var cash = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Chequing",
            AccountType = AccountKind.Cash.ToAccountType(),
            Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(cash);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateDividendRequest(cash.Id, _cad.Id, 10m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonPositiveAmount_Throws()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(
                new CreateDividendRequest(investment.Id, _cad.Id, 0m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_OnDripHalf_Throws()
    {
        // A dividend that is half of a DRIP (RelatedTransactionId set) must not be editable via the
        // standalone dividend editor — that would desync the pair's balance.
        var investment = await AddInvestmentAsync(_cad.Id);

        var created = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _cad.Id, 10m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
            TestContext.Current.CancellationToken);
        created.Transaction!.RelatedTransactionId = Guid.NewGuid();
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Transaction!.Id,
                new UpdateDividendRequest(investment.Id, _cad.Id, 15m,
                    new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_ChangesAmountAndCurrency_RebindsBothLegs()
    {
        var investment = await AddInvestmentAsync(_cad.Id);

        var created = await _sut.CreateAsync(
            new CreateDividendRequest(investment.Id, _cad.Id, 10m,
                new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), "Initial"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await _sut.UpdateAsync(created.Transaction!.Id,
            new UpdateDividendRequest(investment.Id, _usd.Id, 7m,
                new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero), "Updated"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)updated.Transaction!;
        Assert.Equal("Updated", txn.Memo);
        var accountLeg = txn.Splits.Single(s => s.AccountId == investment.Id);
        var incomeLeg = txn.Splits.Single(s => s.AccountId == _income.Id);
        Assert.Equal(7m, accountLeg.Quantity);
        Assert.Equal(_usd.Id, accountLeg.CommodityId);
        Assert.Equal(-7m, incomeLeg.Quantity);
        Assert.Equal(_usd.Id, incomeLeg.CommodityId);
    }
}
