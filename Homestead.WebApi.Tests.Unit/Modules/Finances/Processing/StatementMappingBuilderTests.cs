using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Processing.Statements;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Processing;

public class StatementMappingBuilderTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private Commodity _cad = null!;
    private Account _account = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);

        _account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Chequing",
            AccountType = AccountType.Asset,
            Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id,
            AccountNumber = "1234567890",
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(_account);
        await _db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static StatementIr IrWithClosing(decimal? closing, DateOnly? periodEnd, string? maskedHint = null) => new()
    {
        Kind = StatementKind.Bank,
        Header = new StatementHeaderIr
        {
            AccountNumberMasked = maskedHint,
            PeriodEnd = periodEnd,
            ClosingBalance = closing is { } c ? new MoneyIr(c, "New Balance") : null,
        },
    };

    private static StatementIr IrWithLines(params StatementLineItemIr[] lines) => new()
    {
        Kind = StatementKind.Bank,
        Header = new StatementHeaderIr(),
        LineItems = lines,
    };

    [Fact]
    public void ProposeBalanceEntry_MapsClosingBalanceAndPeriodEnd()
    {
        var ir = IrWithClosing(1234.56m, new DateOnly(2026, 6, 30));

        var proposal = StatementMappingBuilder.ProposeBalanceEntry(ir);

        Assert.NotNull(proposal);
        Assert.Equal(1234.56m, proposal!.AssertedAmount);
        Assert.Equal(new DateOnly(2026, 6, 30), proposal.Date);
        Assert.Equal("New Balance", proposal.SourceAnchor);
        Assert.True(proposal.Accepted);
    }

    [Fact]
    public void ProposeBalanceEntry_ReturnsNull_WhenNoClosingBalance()
    {
        var ir = IrWithClosing(null, new DateOnly(2026, 6, 30));
        Assert.Null(StatementMappingBuilder.ProposeBalanceEntry(ir));
    }

    [Theory]
    [InlineData("1234567890", "••••7890", AccountNumberMatch.Match)]
    [InlineData("1234567890", "••••0000", AccountNumberMatch.Mismatch)]
    [InlineData(null, "••••7890", AccountNumberMatch.Unknown)]
    [InlineData("1234567890", null, AccountNumberMatch.Unknown)]
    [InlineData("1234567890", "••", AccountNumberMatch.Unknown)]  // too few digits in hint
    public void MatchAccountNumber_ClassifiesByComparableSuffix(
        string? stored, string? hint, AccountNumberMatch expected)
    {
        Assert.Equal(expected, StatementMappingBuilder.MatchAccountNumber(stored, hint));
    }

    [Fact]
    public async Task BuildAsync_FlagsDuplicate_WhenAssertionAlreadyExistsForDate()
    {
        var periodEnd = new DateOnly(2026, 6, 30);
        _db.AccountTransactions.Add(new BalanceEntryTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = periodEnd.ToEndOfDayUtc(),
            Memo = "existing",
            Type = TransactionType.BalanceEntry,
            AccountId = _account.Id,
            AssertedCommodityId = _cad.Id,
            AssertedQuantity = 100m,
        });
        await _db.SaveChangesAsync();

        var sut = new StatementMappingBuilder(_db);
        var proposal = await sut.BuildAsync(IrWithClosing(500m, periodEnd, "••••7890"), _account, TestContext.Current.CancellationToken);

        Assert.True(proposal.DuplicateAssertionExists);
        Assert.Equal(AccountNumberMatch.Match, proposal.AccountNumberMatch);
        Assert.NotNull(proposal.BalanceEntry);
    }

    [Fact]
    public async Task BuildAsync_NoDuplicate_WhenNoExistingAssertion()
    {
        var sut = new StatementMappingBuilder(_db);
        var proposal = await sut.BuildAsync(
            IrWithClosing(500m, new DateOnly(2026, 6, 30), "••••7890"), _account, TestContext.Current.CancellationToken);

        Assert.False(proposal.DuplicateAssertionExists);
    }

    [Fact]
    public void ProposeFlows_MapsOutToExpense_AndInToIncome()
    {
        var ir = IrWithLines(
            new StatementLineItemIr(new DateOnly(2026, 6, 3), "GROCERY MART", 42.10m, StatementLineDirection.Out, null, null),
            new StatementLineItemIr(new DateOnly(2026, 6, 15), "PAYROLL DEPOSIT", 2000m, StatementLineDirection.In, 2957.90m, null));

        var flows = StatementMappingBuilder.ProposeFlows(ir);

        Assert.Equal(2, flows.Count);

        Assert.Equal(0, flows[0].Index);
        Assert.Equal(FlowDirection.Expense, flows[0].Direction);
        Assert.Equal(42.10m, flows[0].Amount);
        Assert.Equal("GROCERY MART", flows[0].Description);
        Assert.True(flows[0].Mappable);
        Assert.True(flows[0].Accepted);

        Assert.Equal(1, flows[1].Index);
        Assert.Equal(FlowDirection.Income, flows[1].Direction);
        Assert.Equal(2000m, flows[1].Amount);
        Assert.Equal(2957.90m, flows[1].RunningBalance);
        Assert.True(flows[1].Mappable);
    }

    [Fact]
    public void ProposeFlows_FlagsUnmappable_WhenAmountMissing()
    {
        var ir = IrWithLines(
            new StatementLineItemIr(new DateOnly(2026, 6, 3), "MYSTERY", null, StatementLineDirection.Out, null, null));

        var flows = StatementMappingBuilder.ProposeFlows(ir);

        var flow = Assert.Single(flows);
        Assert.False(flow.Mappable);
        Assert.Equal(0m, flow.Amount);
    }

    [Fact]
    public void ProposeFlows_FlagsUnmappable_WithExpensePlaceholder_WhenDirectionMissing()
    {
        var ir = IrWithLines(
            new StatementLineItemIr(new DateOnly(2026, 6, 3), "MYSTERY", 10m, null, null, null));

        var flows = StatementMappingBuilder.ProposeFlows(ir);

        var flow = Assert.Single(flows);
        Assert.False(flow.Mappable);
        Assert.Equal(FlowDirection.Expense, flow.Direction); // null direction seeds an Expense placeholder
    }

    [Fact]
    public void ProposeFlows_ReturnsEmpty_WhenNoLineItems()
    {
        Assert.Empty(StatementMappingBuilder.ProposeFlows(IrWithClosing(500m, new DateOnly(2026, 6, 30))));
    }

    [Fact]
    public async Task BuildAsync_PopulatesLineItems()
    {
        var ir = IrWithLines(
            new StatementLineItemIr(new DateOnly(2026, 6, 3), "GROCERY", 42.10m, StatementLineDirection.Out, null, null));

        var sut = new StatementMappingBuilder(_db);
        var proposal = await sut.BuildAsync(ir, _account, TestContext.Current.CancellationToken);

        Assert.Single(proposal.LineItems);
        Assert.Equal(FlowDirection.Expense, proposal.LineItems[0].Direction);
    }
}
