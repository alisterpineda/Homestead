using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Reports;

public class GetNetWorthHistoryHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ValuationService _valuations = null!;
    private GetNetWorthHistoryHandler _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;
    private Commodity _veqt = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Exchange = "TSX", Name = "VEQT", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_cad, _usd, _veqt);
        await _db.SaveChangesAsync();

        _valuations = new ValuationService(_db, new StubPriceSourceRegistry());
        _sut = new GetNetWorthHistoryHandler(_db, _valuations);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SetDisplayCurrencyAsync(Guid commodityId)
    {
        var json = JsonSerializer.Serialize(new FinanceSettings { DisplayCurrencyId = commodityId }, FieldChange.JsonOptions);
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.FinancesModule,
            Value = json,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddPriceAsync(Guid commodityId, Guid quoteId, decimal rate, DateTimeOffset priceAt)
    {
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteId,
            Rate = rate,
            PriceAt = priceAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Account> AddAccountAsync(
        AccountKind kind, Guid reportingCurrencyId, string name = "Acct", bool archived = false, bool system = false)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = system ? AccountType.Equity : kind.ToAccountType(),
            Kind = system ? null : kind,
            ReportingCurrencyId = reportingCurrencyId,
            IsSystem = system,
            IsArchived = archived,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private async Task AddSplitAsync(
        Guid accountId, Guid commodityId, decimal quantity, DateTimeOffset occurredAt,
        TransactionType type = TransactionType.Normal)
    {
        AccountTransaction txn = type switch
        {
            TransactionType.OpeningBalance => new OpeningBalanceTransaction { Id = Guid.NewGuid(), OccurredAt = occurredAt, Memo = "ob", Type = type },
            TransactionType.BalanceEntry => new BalanceEntryTransaction { Id = Guid.NewGuid(), OccurredAt = occurredAt, Memo = "be", Type = type, AssertedQuantity = 0m, AssertedCommodityId = commodityId },
            _ => new NormalTransaction { Id = Guid.NewGuid(), OccurredAt = occurredAt, Memo = "txn", Type = type },
        };
        _db.AccountTransactions.Add(txn);
        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = txn.Id,
            AccountId = accountId,
            CommodityId = commodityId,
            Quantity = quantity,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmptyDb_ReturnsNoTransactionsReason()
    {
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(new GetNetWorthHistoryQuery(), Ct);

        Assert.Equal("no_transactions", resp.Reason);
        Assert.Empty(resp.Points);
        Assert.Null(resp.From);
        Assert.False(resp.HasUnpriced);
        Assert.Equal(_cad.Id, resp.DisplayCurrencyId);
        Assert.Equal("CAD", resp.DisplayCurrencySymbol);
    }

    [Fact]
    public async Task NoDisplayCurrency_ReturnsNoDisplayCurrencyReason()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, DateTimeOffset.UtcNow.AddDays(-1));

        var resp = await _sut.Handle(new GetNetWorthHistoryQuery(), Ct);

        Assert.Equal("no_display_currency", resp.Reason);
        Assert.Empty(resp.Points);
        Assert.Null(resp.DisplayCurrencyId);
        Assert.Null(resp.DisplayCurrencySymbol);
    }

    [Fact]
    public async Task SingleAccountDailyRange_BucketsReflectRunningBalance()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        // Three deposits across the range.
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 25m, new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        Assert.Null(resp.Reason);
        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
        // Last point reflects the full running total.
        Assert.Equal(175m, resp.Points[^1].Value);
        Assert.Equal(175m, resp.Points[^1].Assets);
        Assert.Equal(0m, resp.Points[^1].Liabilities);
        // ThisMonth would start at 2026-04-01 but clamps to the earliest activity (2026-04-05)
        // per the design-doc rule that charts begin from the user's earliest recorded event.
        Assert.Equal(new DateTime(2026, 4, 5), resp.Points[0].AsOf.UtcDateTime.Date);
        Assert.Equal(100m, resp.Points[0].Value);
        Assert.Equal(100m, resp.Points[0].Assets);
        Assert.Equal(0m, resp.Points[0].Liabilities);
        // Point on 2026-04-20 reflects first two splits.
        var apr20 = resp.Points.First(p => p.AsOf.UtcDateTime.Date == new DateTime(2026, 4, 20));
        Assert.Equal(150m, apr20.Value);
        Assert.Equal(150m, apr20.Assets);
        Assert.Equal(0m, apr20.Liabilities);
    }

    [Fact]
    public async Task MultiCurrencyAccounts_ConvertsToDisplayCurrency()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cad = await AddAccountAsync(AccountKind.Cash, _cad.Id, "CADcash");
        var usd = await AddAccountAsync(AccountKind.Cash, _usd.Id, "USDcash");
        await AddSplitAsync(cad.Id, _cad.Id, 1000m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(usd.Id, _usd.Id, 500m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await AddPriceAsync(_usd.Id, _cad.Id, 1.30m, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        // 1000 CAD + 500 USD * 1.30 = 1650 CAD
        Assert.Equal(1650m, resp.Points[^1].Value);
        Assert.Equal(1650m, resp.Points[^1].Assets);
        Assert.Equal(0m, resp.Points[^1].Liabilities);
        Assert.False(resp.HasUnpriced);
    }

    [Fact]
    public async Task ArchivedAccount_StillContributesToHistory()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var archived = await AddAccountAsync(AccountKind.Cash, _cad.Id, "OldCash", archived: true);
        await AddSplitAsync(archived.Id, _cad.Id, 500m, new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        Assert.Equal(500m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task SystemAccounts_DoNotContribute()
    {
        // BalanceEntry-style scenario: user account gets +100, system Adjustments gets -100.
        // Net worth should reflect +100 on the user account; the system counterpart drops out.
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        var adjustments = await AddAccountAsync(AccountKind.Cash, _cad.Id, "Equity:Adjustments", system: true);
        var be = new DateTimeOffset(2026, 4, 15, 23, 59, 59, TimeSpan.Zero);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, be, TransactionType.BalanceEntry);
        await AddSplitAsync(adjustments.Id, _cad.Id, -100m, be, TransactionType.BalanceEntry);
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        Assert.Equal(100m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task OpeningBalanceAtNoonUtc_ReflectedInSameDayBucket()
    {
        // OpeningBalances are stored at 12:00 UTC; daily buckets close at 23:59:59 UTC.
        // The bucket for the OB date should already include the OB.
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(
            cash.Id, _cad.Id, 750m,
            new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero),
            TransactionType.OpeningBalance);
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        var openingDay = resp.Points.First(p => p.AsOf.UtcDateTime.Date == new DateTime(2026, 4, 10));
        Assert.Equal(750m, openingDay.Value);
    }

    [Fact]
    public async Task UnpricedCommodity_BucketIsNullAndHasUnpricedFlagSet()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var inv = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        // VEQT has no price recorded; the position is unpriced.
        await AddSplitAsync(inv.Id, _veqt.Id, 50m, new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        Assert.True(resp.HasUnpriced);
        // After the split, every bucket has a non-zero unpriced position → null value.
        var afterSplit = resp.Points.First(p => p.AsOf.UtcDateTime.Date == new DateTime(2026, 4, 5));
        Assert.Null(afterSplit.Value);
        // Asset side carries the unpriced position, so its bucket is also null.
        Assert.Null(afterSplit.Assets);
        // No liability accounts exist — the liability side stays at zero.
        Assert.Equal(0m, afterSplit.Liabilities);
    }

    [Fact]
    public async Task LiabilityAccount_ProducesNegativeLiabilities_AndValueEqualsAssetsPlusLiabilities()
    {
        // Beancount sign: liabilities are stored as negative quantities.
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id, "Cash");
        var card = await AddAccountAsync(AccountKind.CreditCard, _cad.Id, "Card");
        await AddSplitAsync(cash.Id, _cad.Id, 1000m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(card.Id, _cad.Id, -250m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        var last = resp.Points[^1];
        Assert.Equal(1000m, last.Assets);
        Assert.Equal(-250m, last.Liabilities);
        Assert.Equal(750m, last.Value);
        Assert.Equal(last.Assets + last.Liabilities, last.Value);
    }

    [Fact]
    public async Task AllRangeOverOneYear_UsesMonthlyGranularity()
    {
        // Activity 18 months ago → "All" range spans > 365 days → monthly granularity.
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 1000m, to.AddMonths(-18));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.All, AsOf = to }, Ct);

        Assert.Equal(NetWorthGranularity.Monthly, resp.Granularity);
        Assert.True(resp.Points.Count <= 24, $"Expected at most ~24 monthly points, got {resp.Points.Count}");
        Assert.Equal(1000m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task AllRangeUnderOneYear_UsesDailyGranularity()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, to.AddDays(-30));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.All, AsOf = to }, Ct);

        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
    }

    [Fact]
    public async Task PriceChangesOverTime_AreReflectedInHistoricalBuckets()
    {
        // FX rate changes between buckets: USD/CAD 1.20 in early April, 1.40 in late April.
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var usdAcct = await AddAccountAsync(AccountKind.Cash, _usd.Id);
        await AddSplitAsync(usdAcct.Id, _usd.Id, 100m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await AddPriceAsync(_usd.Id, _cad.Id, 1.20m, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await AddPriceAsync(_usd.Id, _cad.Id, 1.40m, new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        // April 10 → uses 1.20 rate → 120 CAD
        var early = resp.Points.First(p => p.AsOf.UtcDateTime.Date == new DateTime(2026, 4, 10));
        Assert.Equal(120m, early.Value);
        // April 25 → uses 1.40 rate → 140 CAD
        var late = resp.Points.First(p => p.AsOf.UtcDateTime.Date == new DateTime(2026, 4, 25));
        Assert.Equal(140m, late.Value);
    }

    [Fact]
    public async Task ThreeMonths_IsCalendarAligned_IncludesCurrentMonth()
    {
        // On 2026-04-30, "Last 3 Months" spans Feb 1 – Apr 30 2026 — three calendar
        // months ending with the current month.
        var to = new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        // Pre-window deposit (carry-over from before Feb 1) — should appear in totals
        // but not as a bucket inside the window.
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2025, 12, 1, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 25m, new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThreeMonths, AsOf = to }, Ct);

        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
        Assert.Equal(new DateTime(2026, 2, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 4, 30), resp.Points[^1].AsOf.UtcDateTime.Date);
        // Carry-over (100) + Feb deposit (50) + Apr deposit (25) = 175.
        Assert.Equal(175m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task TwelveMonths_IsCalendarAligned_IncludesCurrentMonth()
    {
        var to = new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2025, 5, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.TwelveMonths, AsOf = to }, Ct);

        Assert.Equal(new DateTime(2025, 5, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 4, 30), resp.Points[^1].AsOf.UtcDateTime.Date);
    }

    [Fact]
    public async Task ThisMonth_StartsAtFirstOfMonth_WhenEarlierActivityExists()
    {
        // Activity on March 15 ensures earliestActivity precedes April 1, so the
        // first-of-month boundary is the binding lower edge (no clamp shift).
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 200m, new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThisMonth, AsOf = to }, Ct);

        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
        Assert.Equal(new DateTime(2026, 4, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        // Pre-month deposit is reflected from day 1.
        Assert.Equal(200m, resp.Points[0].Value);
        Assert.Equal(250m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task YearToDate_StartsAtJan1()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        // Historical deposit before YTD window establishes a running balance.
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.YearToDate, AsOf = to }, Ct);

        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
        Assert.Equal(new DateTime(2026, 1, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        // Jan 1 reflects the carried-over balance from 2025-06-01.
        Assert.Equal(100m, resp.Points[0].Value);
        Assert.Equal(150m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task LastYear_SpansJan1ToDec31OfPriorYear()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        // Pre-2025 activity ensures the from clamp does not push the window forward.
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2024, 11, 1, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 300m, new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));
        // 2026 activity must not bleed into the LastYear (2025) window.
        await AddSplitAsync(cash.Id, _cad.Id, 9999m, new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.LastYear, AsOf = to }, Ct);

        Assert.Equal(new DateTime(2025, 1, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        // Last bucket lands on the last day of 2025; cutoff is end-of-day UTC.
        Assert.Equal(new DateTime(2025, 12, 31), resp.Points[^1].AsOf.UtcDateTime.Date);
        // Carry-over (100) + 2025-06-15 deposit (300) = 400; 2026 deposit excluded.
        Assert.Equal(400m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task Custom_UsesProvidedFromAndToDates()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 200m, new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                AsOf = to,
                FromDate = new DateOnly(2026, 2, 1),
                ToDate = new DateOnly(2026, 3, 31),
            }, Ct);

        Assert.Equal(NetWorthGranularity.Daily, resp.Granularity);
        Assert.Equal(new DateTime(2026, 2, 1), resp.Points[0].AsOf.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 3, 31), resp.Points[^1].AsOf.UtcDateTime.Date);
        // Pre-window deposit (2026-01-05 = 100) carried forward; mid-window 2026-02-10 += 200.
        Assert.Equal(100m, resp.Points[0].Value);
        Assert.Equal(300m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task Custom_OverYearSpan_UsesMonthlyGranularity()
    {
        var to = new DateTimeOffset(2026, 4, 28, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 1000m, new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                AsOf = to,
                FromDate = new DateOnly(2024, 1, 1),
                ToDate = new DateOnly(2026, 1, 1),
            }, Ct);

        Assert.Equal(NetWorthGranularity.Monthly, resp.Granularity);
    }

    [Fact]
    public async Task Custom_MissingDates_Throws()
    {
        await SetDisplayCurrencyAsync(_cad.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.Custom }, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                FromDate = new DateOnly(2026, 1, 1),
            }, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                ToDate = new DateOnly(2026, 1, 1),
            }, Ct));
    }

    [Fact]
    public async Task Custom_InvertedRange_Throws()
    {
        await SetDisplayCurrencyAsync(_cad.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                FromDate = new DateOnly(2026, 4, 1),
                ToDate = new DateOnly(2026, 1, 1),
            }, Ct));
    }

    [Fact]
    public async Task Custom_FutureToDate_ClampsToAsOf()
    {
        // AsOf is mid-month; ToDate sits at end of a future month. The clamp
        // must keep every point at or before AsOf and pin response.To to AsOf.
        var asOf = new DateTimeOffset(2026, 5, 15, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery
            {
                Range = NetWorthRange.Custom,
                AsOf = asOf,
                FromDate = new DateOnly(2026, 2, 15),
                ToDate = new DateOnly(2026, 8, 31),
            }, Ct);

        Assert.Equal(asOf, resp.To);
        Assert.All(resp.Points, p => Assert.True(p.AsOf <= asOf,
            $"Point {p.AsOf} exceeded AsOf {asOf}"));
        Assert.Equal(asOf, resp.Points[^1].AsOf);
        Assert.Equal(150m, resp.Points[^1].Value);
    }

    [Fact]
    public async Task ThreeMonths_MidMonthAsOf_ClampsToAsOf()
    {
        // ThreeMonths normally extends to end-of-current-month. With a mid-month
        // AsOf the clamp must trim future days so no point sits past AsOf.
        var asOf = new DateTimeOffset(2026, 5, 15, 23, 59, 59, TimeSpan.Zero);
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 100m, new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(cash.Id, _cad.Id, 50m, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        await SetDisplayCurrencyAsync(_cad.Id);

        var resp = await _sut.Handle(
            new GetNetWorthHistoryQuery { Range = NetWorthRange.ThreeMonths, AsOf = asOf }, Ct);

        Assert.Equal(asOf, resp.To);
        Assert.All(resp.Points, p => Assert.True(p.AsOf <= asOf,
            $"Point {p.AsOf} exceeded AsOf {asOf}"));
        Assert.Equal(new DateTime(2026, 5, 15), resp.Points[^1].AsOf.UtcDateTime.Date);
    }
}
