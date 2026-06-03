using System.Text.Json;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class DividendsEndpointTests : IntegrationTestBase
{
    public DividendsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> GetIncomeAccountIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Accounts.Where(a => a.IsSystem && a.Name == "Income").Select(a => a.Id).SingleAsync(CT);
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

    [Fact]
    public async Task Create_Dividend_RoundTrip_PostsToIncome_NoPriceRow()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var income = await GetIncomeAccountIdAsync();
        var account = await CreateInvestmentAccountAsync(client, cad, "Div-RRSP");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/dividends", new
        {
            accountId = account,
            paymentCurrencyId = cad,
            amount = 12.50m,
            occurredAt = "2026-04-23T12:00:00Z",
            description = "Q1 distribution",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Dividend", body.GetProperty("shape").GetString());

        var details = body.GetProperty("details");
        Assert.Equal("dividend", details.GetProperty("kind").GetString());
        Assert.Equal(account, details.GetProperty("accountId").GetGuid());
        Assert.Equal(cad, details.GetProperty("paymentCurrencyId").GetGuid());
        Assert.Equal(12.50m, details.GetProperty("amount").GetDecimal());

        var id = body.GetProperty("id").GetGuid();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        var accountLeg = splits.Single(s => s.AccountId == account);
        var incomeLeg = splits.Single(s => s.AccountId == income);
        Assert.Equal(12.50m, accountLeg.Quantity);
        Assert.Equal(-12.50m, incomeLeg.Quantity);
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == id, CT));
    }

    [Fact]
    public async Task Create_Dividend_BlankDescription_DefaultsToDividend()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateInvestmentAccountAsync(client, cad, "Default-RRSP");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/dividends", new
        {
            accountId = account,
            paymentCurrencyId = cad,
            amount = 5m,
            occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Dividend", body.GetProperty("memo").GetString());
    }

    [Fact]
    public async Task Patch_Dividend_UpdatesAmount()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var income = await GetIncomeAccountIdAsync();
        var account = await CreateInvestmentAccountAsync(client, cad, "PatchDiv-RRSP");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/dividends", new
        {
            accountId = account,
            paymentCurrencyId = cad,
            amount = 10m,
            occurredAt = "2026-04-23T12:00:00Z",
            description = "Initial",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var id = created.GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/dividends/{id}", new
            {
                accountId = account,
                paymentCurrencyId = cad,
                amount = 15m,
                occurredAt = "2026-04-23T12:00:00Z",
                description = "Revised",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits.Where(s => s.TransactionId == id).ToListAsync(CT);
        Assert.Equal(15m, splits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(-15m, splits.Single(s => s.AccountId == income).Quantity);
    }

    [Fact]
    public async Task Create_Dividend_OnNonInvestmentAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing-Div",
            kind = "Cash",
            reportingCurrencyId = cad,
        }, CT);
        var cash = await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/dividends", new
        {
            accountId = cash!.Id,
            paymentCurrencyId = cad,
            amount = 5m,
            occurredAt = "2026-04-23T12:00:00Z",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
