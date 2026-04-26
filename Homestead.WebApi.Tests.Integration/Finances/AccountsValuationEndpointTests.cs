using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class AccountsValuationEndpointTests : IntegrationTestBase
{
    public AccountsValuationEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task SetDisplayCurrencyAsync(HttpClient client, Guid commodityId)
    {
        var resp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)commodityId.ToString() }
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, Guid currencyId, string name,
        decimal initial = 0m, string asOfDate = "2026-01-01")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = currencyId,
            initialBalance = new { amount = initial, asOfDate },
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateInvestmentAccountAsync(HttpClient client, Guid currencyId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Investment",
            reportingCurrencyId = currencyId,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateSecurityAsync(HttpClient client, string symbol, string exchange = "TSX")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol,
            exchange,
            name = symbol,
            displayPrecision = 4,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);
        return body!.Id;
    }

    private async Task CreatePriceAsync(HttpClient client, Guid commodityId, Guid quoteId,
        decimal rate, DateTimeOffset priceAt)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/commodity-prices", new
        {
            commodityId,
            quoteCurrencyId = quoteId,
            priceAt,
            rate,
        }, CT);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<FinanceAccountItem> GetAccountAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/finances/accounts/{id}", CT);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
    }

    [Fact]
    public async Task GetAccount_CashInDisplayCurrency_DisplayValueEqualsNative()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var cashId = await CreateCashAccountAsync(client, cad, "Chequing", 1000m);

        var body = await GetAccountAsync(client, cashId);
        Assert.Equal(1000m, body.NativeBalance);
        Assert.Equal(1000m, body.DisplayValue);
        Assert.Equal(0, body.UnpricedHoldingCount);
    }

    [Fact]
    public async Task GetAccount_CashInOtherCurrency_DisplayValueUsesPriceConversion()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var usd = await GetCurrencyIdAsync("USD");
        await SetDisplayCurrencyAsync(client, cad);
        await CreatePriceAsync(client, usd, cad, 1.30m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var usdCash = await CreateCashAccountAsync(client, usd, "USD Cash", 100m);

        var body = await GetAccountAsync(client, usdCash);
        Assert.Equal(100m, body.NativeBalance);
        Assert.Equal(130m, body.DisplayValue);
        Assert.Equal(0, body.UnpricedHoldingCount);
    }

    [Fact]
    public async Task GetAccount_CreditCardWithBalance_KeepsBeancountSign()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Visa",
            kind = "CreditCard",
            reportingCurrencyId = cad,
            initialBalance = new { amount = 500m, asOfDate = "2026-01-01" },
        }, CT);
        var created = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var body = await GetAccountAsync(client, created!.Id);
        Assert.Equal(-500m, body.NativeBalance);
        Assert.Equal(-500m, body.DisplayValue);
    }

    [Fact]
    public async Task GetAccount_InvestmentWithUnpricedHolding_DisplayValueIsNull()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var veqt = await CreateSecurityAsync(client, "VEQT");
        await SetDisplayCurrencyAsync(client, cad);

        var inv = await CreateInvestmentAccountAsync(client, cad, "RRSP");
        // Add an unpriced position via opening balance (no per-unit price).
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{inv}/opening-balances",
            new { commodityId = veqt, quantity = 50m, asOfDate = "2026-01-01" }, CT);
        resp.EnsureSuccessStatusCode();

        var body = await GetAccountAsync(client, inv);
        Assert.Null(body.NativeBalance);            // Investment kinds always null
        Assert.Null(body.DisplayValue);              // all-unpriced → null
        Assert.Equal(1, body.UnpricedHoldingCount);
    }

    [Fact]
    public async Task GetAccount_InvestmentWithMixedPricedAndUnpriced_PartialSumWithCount()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var veqt = await CreateSecurityAsync(client, "VEQT");
        await SetDisplayCurrencyAsync(client, cad);

        var inv = await CreateInvestmentAccountAsync(client, cad, "RRSP");
        // Priced position: cash CAD inside the investment account.
        var cashOpen = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{inv}/opening-balances",
            new { commodityId = cad, quantity = 1000m, asOfDate = "2026-01-01" }, CT);
        cashOpen.EnsureSuccessStatusCode();
        // Unpriced security position.
        var secOpen = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{inv}/opening-balances",
            new { commodityId = veqt, quantity = 50m, asOfDate = "2026-01-01" }, CT);
        secOpen.EnsureSuccessStatusCode();

        var body = await GetAccountAsync(client, inv);
        Assert.Equal(1000m, body.DisplayValue);      // priced subset
        Assert.Equal(1, body.UnpricedHoldingCount);
        Assert.Null(body.NativeBalance);
    }

    [Fact]
    public async Task GetAccounts_ListIncludesDisplayValueAndCount()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        await SetDisplayCurrencyAsync(client, cad);

        await CreateCashAccountAsync(client, cad, "Acct1", 100m);
        await CreateCashAccountAsync(client, cad, "Acct2", 200m);

        var resp = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=100&includeArchived=true", CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);

        var a1 = Assert.Single(body!.Items, a => a.Name == "Acct1");
        Assert.Equal(100m, a1.DisplayValue);
        Assert.Equal(100m, a1.NativeBalance);
        var a2 = Assert.Single(body.Items, a => a.Name == "Acct2");
        Assert.Equal(200m, a2.DisplayValue);
    }

    [Fact]
    public async Task GetAccount_DisplayCurrencyUnset_DisplayValueIsNullForAnyHolding()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        // Deliberately do NOT set display currency.

        var cashId = await CreateCashAccountAsync(client, cad, "Chequing", 1000m);

        var body = await GetAccountAsync(client, cashId);
        Assert.Equal(1000m, body.NativeBalance);    // unaffected
        Assert.Null(body.DisplayValue);              // no display currency configured
        Assert.Equal(1, body.UnpricedHoldingCount);
    }
}
