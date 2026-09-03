using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Tests.Unit.Modules.Finances;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class ValuationServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private StubPriceSourceRegistry _sources = null!;
    private ValuationService _sut = null!;

    private Commodity _cad = null!;
    private Commodity _usd = null!;
    private Commodity _eur = null!;
    private Commodity _veqt = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();

        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _eur = new Commodity { Id = Guid.NewGuid(), Symbol = "EUR", Name = "EUR", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _veqt = new Commodity { Id = Guid.NewGuid(), Symbol = "VEQT", Exchange = "TSX", Name = "VEQT", Kind = CommodityKind.Security, DisplayPrecision = 4 };
        _db.Commodities.AddRange(_cad, _usd, _eur, _veqt);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _sources = new StubPriceSourceRegistry();
        _sut = new ValuationService(_db, _sources);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AddPriceAsync(
        Guid commodityId,
        Guid quoteId,
        decimal rate,
        DateTimeOffset priceAt,
        CommodityPriceSource source = default,
        DateTimeOffset? createdAt = null)
    {
        var stamp = createdAt ?? DateTimeOffset.UtcNow;
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteId,
            Rate = rate,
            PriceAt = priceAt,
            Source = source,
            CreatedAt = stamp,
            UpdatedAt = stamp,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // Enablement is static config ("PriceSources:{Source}:Enabled"), so the stub registry is toggled
    // directly rather than via a settings row.
    private void SetSourceEnabled(CommodityPriceSource source, bool isEnabled) =>
        _sources.Set(source, enabled: isEnabled, configured: isEnabled);

    private async Task<Account> AddAccountAsync(AccountKind kind, Guid reportingCurrencyId, string name = "Acct")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = kind.ToAccountType(),
            Kind = kind,
            ReportingCurrencyId = reportingCurrencyId,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account;
    }

    private async Task AddSplitAsync(Guid accountId, Guid commodityId, decimal quantity,
        DateTimeOffset? occurredAt = null)
    {
        var txn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow.AddDays(-1),
            Memo = "test",
            Type = TransactionType.Normal,
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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveRate_SameCommodity_QuantityPassesThroughUnchanged()
    {
        await SetDisplayCurrencyAsync(_cad.Id);
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100m, ctx.ConvertToDisplay(_cad.Id, 100m));
    }

    [Fact]
    public async Task LoadContextAsync_NormalizesNonUtcAsOfToUtc()
    {
        // Npgsql's `timestamp with time zone` requires offset 0 — local-offset values
        // from the frontend (e.g. -06:00) must be normalized before any EF query runs.
        await SetDisplayCurrencyAsync(_cad.Id);
        var localOffset = new DateTimeOffset(2026, 5, 1, 23, 59, 59, 999, TimeSpan.FromHours(-6));

        var ctx = await _sut.LoadContextAsync(localOffset, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.Zero, ctx.AsOf.Offset);
        Assert.Equal(localOffset.UtcDateTime, ctx.AsOf.UtcDateTime);
    }

    [Fact]
    public async Task ResolveRate_DirectLookup()
    {
        // 1 USD = 1.35 CAD
        await AddPriceAsync(_usd.Id, _cad.Id, 1.35m, DateTimeOffset.UtcNow.AddDays(-2));
        await SetDisplayCurrencyAsync(_cad.Id);
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(135m, ctx.ConvertToDisplay(_usd.Id, 100m));
    }

    [Fact]
    public async Task ResolveRate_InvertedLookup()
    {
        // Only the reverse pair is stored: 1 CAD = 0.74 USD → 1 USD = 1/0.74 CAD ≈ 1.3513...
        await AddPriceAsync(_cad.Id, _usd.Id, 0.74m, DateTimeOffset.UtcNow.AddDays(-2));
        await SetDisplayCurrencyAsync(_cad.Id);
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        var result = ctx.ConvertToDisplay(_usd.Id, 100m);
        Assert.NotNull(result);
        Assert.Equal(100m / 0.74m, result!.Value);
    }

    [Fact]
    public async Task ResolveRate_OneHopChain()
    {
        // EUR → USD → CAD. No direct EUR/CAD pair.
        await AddPriceAsync(_eur.Id, _usd.Id, 1.10m, DateTimeOffset.UtcNow.AddDays(-2));
        await AddPriceAsync(_usd.Id, _cad.Id, 1.35m, DateTimeOffset.UtcNow.AddDays(-2));
        await SetDisplayCurrencyAsync(_cad.Id);
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(100m * 1.10m * 1.35m, ctx.ConvertToDisplay(_eur.Id, 100m));
    }

    [Fact]
    public async Task ResolveRate_NoPath_ReturnsNull()
    {
        // EUR has no path to CAD.
        await SetDisplayCurrencyAsync(_cad.Id);
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Null(ctx.ConvertToDisplay(_eur.Id, 100m));
    }

    [Fact]
    public async Task ResolveRate_NoDisplayCurrency_ReturnsNull()
    {
        // Settings unset; even same-commodity should return null because there is no display currency.
        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Null(ctx.ConvertToDisplay(_cad.Id, 100m));
        Assert.Null(ctx.DisplayCurrencyId);
    }

    [Fact]
    public async Task ValueAccount_EmptyAccount_ReturnsZeroDisplayAndNativeAndZeroCount()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(cash, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(0m, v.DisplayValue);
        Assert.Equal(0m, v.NativeBalance);
        Assert.Equal(0, v.UnpricedHoldingCount);
        Assert.Empty(v.Positions);
    }

    [Fact]
    public async Task ValueAccount_CashInDisplayCurrency_DisplayEqualsNative()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 1500m);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(cash, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(1500m, v.DisplayValue);
        Assert.Equal(1500m, v.NativeBalance);
        Assert.Equal(0, v.UnpricedHoldingCount);
    }

    [Fact]
    public async Task ValueAccount_CashInOtherCurrency_DisplayUsesFx()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _usd.Id);
        await AddSplitAsync(cash.Id, _usd.Id, 100m);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.35m, DateTimeOffset.UtcNow.AddDays(-2));
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(cash, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(135m, v.DisplayValue);
        Assert.Equal(100m, v.NativeBalance);
        Assert.Equal(0, v.UnpricedHoldingCount);
    }

    [Fact]
    public async Task ValueAccount_LiabilityKeepsBeancountSign()
    {
        // CreditCard accounts store balances negatively; the wire surface keeps that sign.
        var cc = await AddAccountAsync(AccountKind.CreditCard, _cad.Id);
        await AddSplitAsync(cc.Id, _cad.Id, -500m);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(cc, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(-500m, v.DisplayValue);
        Assert.Equal(-500m, v.NativeBalance);
    }

    [Fact]
    public async Task ValueAccount_Investment_NativeBalanceIsNull()
    {
        var inv = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        await AddSplitAsync(inv.Id, _cad.Id, 1000m);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(inv, ctx, TestContext.Current.CancellationToken);

        Assert.Null(v.NativeBalance);
        Assert.Equal(1000m, v.DisplayValue);
    }

    [Fact]
    public async Task ValueAccount_AllUnpriced_DisplayValueIsNull()
    {
        var inv = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        await AddSplitAsync(inv.Id, _veqt.Id, 50m);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(inv, ctx, TestContext.Current.CancellationToken);

        Assert.Null(v.DisplayValue);
        Assert.Equal(1, v.UnpricedHoldingCount);
        var pos = Assert.Single(v.Positions);
        Assert.False(pos.IsPriced);
    }

    [Fact]
    public async Task ValueAccount_MixedPricedAndUnpriced_PartialSumWithCount()
    {
        var inv = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        await AddSplitAsync(inv.Id, _cad.Id, 1000m);     // priced (display = CAD)
        await AddSplitAsync(inv.Id, _veqt.Id, 50m);       // unpriced
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(inv, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(1000m, v.DisplayValue);
        Assert.Equal(1, v.UnpricedHoldingCount);
        Assert.Equal(2, v.Positions.Count);
    }

    [Fact]
    public async Task ValueAccount_FuturedDatedSplitsAreExcluded()
    {
        var cash = await AddAccountAsync(AccountKind.Cash, _cad.Id);
        await AddSplitAsync(cash.Id, _cad.Id, 1000m, DateTimeOffset.UtcNow.AddDays(-1));
        await AddSplitAsync(cash.Id, _cad.Id, 500m, DateTimeOffset.UtcNow.AddDays(+30));
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(cash, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(1000m, v.NativeBalance);
        Assert.Equal(1000m, v.DisplayValue);
    }

    [Fact]
    public async Task ValueAccount_NetZeroPositionDoesNotCountAsHolding()
    {
        var inv = await AddAccountAsync(AccountKind.Investment, _cad.Id);
        await AddSplitAsync(inv.Id, _veqt.Id, 50m);
        await AddSplitAsync(inv.Id, _veqt.Id, -50m);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var v = await _sut.ValueAccountAsync(inv, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(0m, v.DisplayValue);
        Assert.Equal(0, v.UnpricedHoldingCount);
        Assert.Empty(v.Positions);
    }

    [Fact]
    public async Task ValueAccounts_BatchedReturnsSameResultsAsPerAccountCalls()
    {
        var a1 = await AddAccountAsync(AccountKind.Cash, _cad.Id, "A");
        var a2 = await AddAccountAsync(AccountKind.Cash, _usd.Id, "B");
        await AddSplitAsync(a1.Id, _cad.Id, 100m);
        await AddSplitAsync(a2.Id, _usd.Id, 200m);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.30m, DateTimeOffset.UtcNow.AddDays(-1));
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        var batched = await _sut.ValueAccountsAsync(new[] { a1, a2 }, ctx, TestContext.Current.CancellationToken);

        Assert.Equal(100m, batched[a1.Id].DisplayValue);
        Assert.Equal(260m, batched[a2.Id].DisplayValue);
    }

    [Fact]
    public async Task LoadContext_DisabledExternalSource_IsExcludedThenReincludedWhenEnabled()
    {
        // BankOfCanada price exists, but the source is disabled → should not be used.
        var priceAt = DateTimeOffset.UtcNow.AddDays(-1);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.35m, priceAt, CommodityPriceSource.BankOfCanada);
        SetSourceEnabled(CommodityPriceSource.BankOfCanada, isEnabled: false);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctxDisabled = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        Assert.Null(ctxDisabled.ConvertToDisplay(_usd.Id, 100m));

        // Toggle on → restored.
        SetSourceEnabled(CommodityPriceSource.BankOfCanada, isEnabled: true);

        var ctxEnabled = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(135m, ctxEnabled.ConvertToDisplay(_usd.Id, 100m));
    }

    [Fact]
    public async Task LoadContext_SamePriceAt_ManualOutranksTransactionOutranksExternal()
    {
        var priceAt = DateTimeOffset.UtcNow.AddDays(-1);
        // Insert in an order that does NOT match the spec priority — depending on
        // insertion order would otherwise yield the wrong winner.
        await AddPriceAsync(_usd.Id, _cad.Id, 1.10m, priceAt, CommodityPriceSource.BankOfCanada);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.20m, priceAt, CommodityPriceSource.Transaction);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.30m, priceAt, CommodityPriceSource.Manual);
        SetSourceEnabled(CommodityPriceSource.BankOfCanada, isEnabled: true);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        // Manual wins.
        Assert.Equal(130m, ctx.ConvertToDisplay(_usd.Id, 100m));
    }

    [Fact]
    public async Task LoadContext_SamePriceAtAndKind_MostRecentlyCreatedWins()
    {
        var priceAt = DateTimeOffset.UtcNow.AddDays(-1);
        var earlier = DateTimeOffset.UtcNow.AddHours(-2);
        var later = DateTimeOffset.UtcNow.AddHours(-1);

        // Both Manual at the same PriceAt — most-recently-created (later) should win.
        await AddPriceAsync(_usd.Id, _cad.Id, 1.10m, priceAt, CommodityPriceSource.Manual, createdAt: earlier);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.20m, priceAt, CommodityPriceSource.Manual, createdAt: later);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(120m, ctx.ConvertToDisplay(_usd.Id, 100m));
    }

    [Fact]
    public async Task LoadContext_ManualOverrideAtSamePriceAtAsExternal_IsUsedForValuation()
    {
        // Durable Manual override workflow: an imported external row and a Manual row at the
        // same instant — valuation must use the Manual rate.
        var priceAt = DateTimeOffset.UtcNow.AddDays(-1);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.35m, priceAt, CommodityPriceSource.BankOfCanada);
        await AddPriceAsync(_usd.Id, _cad.Id, 1.40m, priceAt, CommodityPriceSource.Manual);
        SetSourceEnabled(CommodityPriceSource.BankOfCanada, isEnabled: true);
        await SetDisplayCurrencyAsync(_cad.Id);

        var ctx = await _sut.LoadContextAsync(TestContext.Current.CancellationToken);

        Assert.Equal(140m, ctx.ConvertToDisplay(_usd.Id, 100m));
    }
}
