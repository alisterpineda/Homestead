using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class FlowServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IFlowCounterpartResolver _counterparts = null!;
    private IAuditContext _audit = null!;
    private FlowService _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;
    private Account _expense = null!;
    private Account _income = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.AddRange(_cad, _usd);

        _expense = SystemAccount("Expense", AccountType.Expense);
        _income = SystemAccount("Income", AccountType.Income);
        _db.Accounts.AddRange(_expense, _income);
        await _db.SaveChangesAsync();

        _counterparts = Substitute.For<IFlowCounterpartResolver>();
        _counterparts.ResolveAsync(FlowDirection.Expense, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_expense);
        _counterparts.ResolveAsync(FlowDirection.Income, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_income);

        _audit = Substitute.For<IAuditContext>();
        _sut = new FlowService(_db, _counterparts, new TransactionValidator(_db), _audit);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private Account SystemAccount(string name, AccountType type) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        AccountType = type,
        Kind = null,
        ReportingCurrencyId = _cad.Id,
        IsSystem = true,
        IsArchived = false,
    };

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

    private CreateFlowRequest Create(FlowDirection direction, Guid accountId, decimal amount,
        DateOnly? date = null, string? description = null) =>
        new(direction, accountId, amount, date ?? new DateOnly(2026, 4, 23), description);

    // ---- Sign correctness: Expense/Income on a Cash (asset) AND a Credit Card (liability) account ----

    [Theory]
    [InlineData(AccountKind.Cash)]
    [InlineData(AccountKind.CreditCard)]
    public async Task Create_Expense_DrainsAccount_LandsOnExpense_BalancesPerCommodity(AccountKind kind)
    {
        var account = await AddAccount(kind, _cad.Id, "Acct");

        var result = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 50m), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.Equal(NormalShape.Expense, txn.NormalShape);

        var accountLeg = txn.Splits.Single(s => s.AccountId == account.Id);
        var expenseLeg = txn.Splits.Single(s => s.AccountId == _expense.Id);

        // Same signs regardless of asset/liability — the plan's no-sign-flip invariant.
        Assert.Equal(-50m, accountLeg.Quantity);
        Assert.Equal(_cad.Id, accountLeg.CommodityId);
        Assert.Equal(50m, expenseLeg.Quantity);
        Assert.Equal(_cad.Id, expenseLeg.CommodityId);

        // Per-commodity balance is zero (validator already enforces this; assert explicitly).
        Assert.Equal(0m, txn.Splits.Where(s => s.CommodityId == _cad.Id).Sum(s => s.Quantity));
    }

    [Theory]
    [InlineData(AccountKind.Cash)]
    [InlineData(AccountKind.CreditCard)]
    public async Task Create_Income_CreditsAccount_LandsOnIncome_BalancesPerCommodity(AccountKind kind)
    {
        var account = await AddAccount(kind, _cad.Id, "Acct");

        var result = await _sut.CreateAsync(
            Create(FlowDirection.Income, account.Id, 30m), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.Equal(NormalShape.Income, txn.NormalShape);

        var accountLeg = txn.Splits.Single(s => s.AccountId == account.Id);
        var incomeLeg = txn.Splits.Single(s => s.AccountId == _income.Id);

        Assert.Equal(30m, accountLeg.Quantity);
        Assert.Equal(-30m, incomeLeg.Quantity);
        Assert.Equal(0m, txn.Splits.Where(s => s.CommodityId == _cad.Id).Sum(s => s.Quantity));
    }

    [Fact]
    public async Task Create_InfersCurrencyFromAccount_AndDatesAtStartOfDayUtc()
    {
        var account = await AddAccount(AccountKind.Cash, _usd.Id, "USD Chequing");

        var result = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 12m, new DateOnly(2026, 4, 23)),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)result.Transaction!;
        Assert.All(txn.Splits, s => Assert.Equal(_usd.Id, s.CommodityId));
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero), txn.OccurredAt);
    }

    [Fact]
    public async Task Create_BlankDescription_StoresEmptyMemo()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        var result = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 10m, description: "   "),
            TestContext.Current.CancellationToken);

        // No forced default — the memo is the searchable free-text line; empty is allowed.
        Assert.Equal("", result.Transaction!.Memo);
    }

    [Fact]
    public async Task Create_NonBlankDescription_PersistsTrimmed()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        var result = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 10m, description: "  Groceries  "),
            TestContext.Current.CancellationToken);

        Assert.Equal("Groceries", result.Transaction!.Memo);
    }

    // ---- Rejections ----

    [Fact]
    public async Task Create_InvestmentAccount_Throws()
    {
        var investment = await AddAccount(AccountKind.Investment, _cad.Id, "RRSP");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Create(FlowDirection.Expense, investment.Id, 10m),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_SystemAccount_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Create(FlowDirection.Expense, _expense.Id, 10m),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Throws()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Old");
        account.IsArchived = true;
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Create(FlowDirection.Expense, account.Id, 10m),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_NonPositiveAmount_Throws()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.CreateAsync(Create(FlowDirection.Expense, account.Id, 0m),
                TestContext.Current.CancellationToken));
    }

    // ---- Update ----

    [Fact]
    public async Task Update_ChangesAmountDateDescription_RebindsBothLegs_KeepsDirection()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");

        var created = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 50m, new DateOnly(2026, 4, 23), "Groceries"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updated = await _sut.UpdateAsync(created.Transaction!.Id,
            new UpdateFlowRequest(account.Id, 65m, new DateOnly(2026, 4, 24), "Groceries + gas"),
            TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var txn = (NormalTransaction)updated.Transaction!;
        Assert.Equal(NormalShape.Expense, txn.NormalShape);
        Assert.Equal("Groceries + gas", txn.Memo);
        Assert.Equal(new DateTimeOffset(2026, 4, 24, 0, 0, 0, TimeSpan.Zero), txn.OccurredAt);

        var accountLeg = txn.Splits.Single(s => s.AccountId == account.Id);
        var expenseLeg = txn.Splits.Single(s => s.AccountId == _expense.Id);
        Assert.Equal(-65m, accountLeg.Quantity);
        Assert.Equal(65m, expenseLeg.Quantity);
    }

    [Fact]
    public async Task Update_ChangingAccount_Throws()
    {
        var account = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");
        var other = await AddAccount(AccountKind.Cash, _cad.Id, "Savings");

        var created = await _sut.CreateAsync(
            Create(FlowDirection.Expense, account.Id, 50m), TestContext.Current.CancellationToken);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(created.Transaction!.Id,
                new UpdateFlowRequest(other.Id, 50m, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_OnNonFlowTransaction_Throws()
    {
        // A transfer is also a 2-split NormalTransaction; the flow editor must refuse it.
        var chequing = await AddAccount(AccountKind.Cash, _cad.Id, "Chequing");
        var savings = await AddAccount(AccountKind.Cash, _cad.Id, "Savings");
        var transfer = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            Memo = "",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Transfer,
            OccurredAt = new DateTimeOffset(2026, 4, 23, 0, 0, 0, TimeSpan.Zero),
            Splits =
            [
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = chequing.Id, CommodityId = _cad.Id, Quantity = -10m },
                new AccountTransactionSplit { Id = Guid.NewGuid(), AccountId = savings.Id, CommodityId = _cad.Id, Quantity = 10m },
            ],
        };
        _db.AccountTransactions.Add(transfer);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.UpdateAsync(transfer.Id,
                new UpdateFlowRequest(chequing.Id, 10m, new DateOnly(2026, 4, 23), null),
                TestContext.Current.CancellationToken));
    }
}

public class FlowCounterpartResolverTests
{
    [Fact]
    public async Task Resolve_Expense_ReturnsSystemExpense()
    {
        var expense = new Account { Id = Guid.NewGuid(), Name = "Expense", AccountType = AccountType.Expense, ReportingCurrencyId = Guid.NewGuid(), IsSystem = true };
        var income = new Account { Id = Guid.NewGuid(), Name = "Income", AccountType = AccountType.Income, ReportingCurrencyId = Guid.NewGuid(), IsSystem = true };
        var provider = Substitute.For<ISystemAccountProvider>();
        provider.GetExpenseAsync(Arg.Any<CancellationToken>()).Returns(expense);
        provider.GetIncomeAsync(Arg.Any<CancellationToken>()).Returns(income);
        var sut = new FlowCounterpartResolver(provider);

        var expenseResult = await sut.ResolveAsync(FlowDirection.Expense, null, TestContext.Current.CancellationToken);
        var incomeResult = await sut.ResolveAsync(FlowDirection.Income, null, TestContext.Current.CancellationToken);

        Assert.Same(expense, expenseResult);
        Assert.Same(income, incomeResult);
    }
}
