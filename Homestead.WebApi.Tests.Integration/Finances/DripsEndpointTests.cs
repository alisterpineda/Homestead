using System.Text.Json;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class DripsEndpointTests : IntegrationTestBase
{
    public DripsEndpointTests(PostgresFixture fixture) : base(fixture) { }

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

    private async Task<Guid> CreateSecurityAsync(HttpClient client, string symbol, string exchange = "NYSEARCA")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol,
            exchange,
            name = $"{symbol} fund",
            displayPrecision = 4,
        }, CT);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        return body.GetProperty("id").GetGuid();
    }

    private static object DripBody(Guid account, Guid security, Guid currency,
        decimal amount, decimal units, string occurredAt, string? description = null) => new
        {
            accountId = account,
            securityId = security,
            paymentCurrencyId = currency,
            amount,
            units,
            occurredAt,
            description,
        };

    [Fact]
    public async Task Create_Drip_RoundTrip_LinksHalves_EmitsSinglePriceRow()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var income = await GetIncomeAccountIdAsync();
        var account = await CreateInvestmentAccountAsync(client, usd, "Drip-RRSP");
        var vfv = await CreateSecurityAsync(client, "VFV");

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z", "Reinvested distribution"), CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(CT);

        // The create returns the Dividend half, linked to the Buy half.
        Assert.Equal("Dividend", body.GetProperty("shape").GetString());
        var dividendId = body.GetProperty("id").GetGuid();
        var buyId = body.GetProperty("relatedTransactionId").GetGuid();
        Assert.NotEqual(Guid.Empty, buyId);
        Assert.Equal(5m, body.GetProperty("details").GetProperty("amount").GetDecimal());

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both halves persisted in one SaveChanges with mutual RelatedTransactionId resolved (the EF
        // nullable-FK insert-cycle).
        var dividend = await db.AccountTransactions.SingleAsync(t => t.Id == dividendId, CT);
        var buy = await db.AccountTransactions.SingleAsync(t => t.Id == buyId, CT);
        Assert.Equal(buyId, dividend.RelatedTransactionId);
        Assert.Equal(dividendId, buy.RelatedTransactionId);

        // Dividend half: +5 into the account, -5 to Income.
        var divSplits = await db.AccountTransactionSplits.Where(s => s.TransactionId == dividendId).ToListAsync(CT);
        Assert.Equal(5m, divSplits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(-5m, divSplits.Single(s => s.AccountId == income).Quantity);

        // Exactly one linked price row, emitted by the Buy half only.
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == dividendId, CT));
        var price = await db.CommodityPrices.SingleAsync(p => p.CreatedByTransactionId == buyId, CT);
        Assert.Equal(vfv, price.CommodityId);
        Assert.Equal(usd, price.QuoteCurrencyId);
        Assert.Equal(100m, price.Rate);   // 5 / 0.05

        // Both halves round-trip via GET with their own shapes/details.
        var getBuy = await client.GetAsync($"/api/v1/finances/transactions/{buyId}", CT);
        var fetchedBuy = await getBuy.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("InvestmentBuy", fetchedBuy.GetProperty("shape").GetString());
        Assert.Equal(dividendId, fetchedBuy.GetProperty("relatedTransactionId").GetGuid());
        var buyDetails = fetchedBuy.GetProperty("details");
        Assert.Equal("trade", buyDetails.GetProperty("kind").GetString());
        Assert.Equal(0.05m, buyDetails.GetProperty("quantity").GetDecimal());
        Assert.Equal(100m, buyDetails.GetProperty("unitPrice").GetDecimal());

        var getDiv = await client.GetAsync($"/api/v1/finances/transactions/{dividendId}", CT);
        var fetchedDiv = await getDiv.Content.ReadFromJsonAsync<JsonElement>(CT);
        Assert.Equal("Dividend", fetchedDiv.GetProperty("shape").GetString());
        Assert.Equal(buyId, fetchedDiv.GetProperty("relatedTransactionId").GetGuid());
    }

    [Fact]
    public async Task Patch_Drip_EditsBothHalvesAtomically_MovesPriceRow()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var income = await GetIncomeAccountIdAsync();
        var account = await CreateInvestmentAccountAsync(client, usd, "PatchDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "PVFV");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var dividendId = created.GetProperty("id").GetGuid();
        var buyId = created.GetProperty("relatedTransactionId").GetGuid();

        // Edit via the clicked (Dividend) half: amount 12, units 0.1, new date.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/drips/{dividendId}",
            DripBody(account, vfv, usd, 12m, 0.1m, "2026-05-01T12:00:00Z", "Revised"), CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both halves moved atomically.
        var divSplits = await db.AccountTransactionSplits.Where(s => s.TransactionId == dividendId).ToListAsync(CT);
        Assert.Equal(12m, divSplits.Single(s => s.AccountId == account).Quantity);
        Assert.Equal(-12m, divSplits.Single(s => s.AccountId == income).Quantity);

        var buySplits = await db.AccountTransactionSplits.Where(s => s.TransactionId == buyId).ToListAsync(CT);
        Assert.Equal(-12m, buySplits.Single(s => s.CommodityId == usd).Quantity);
        var sec = buySplits.Single(s => s.CommodityId == vfv);
        Assert.Equal(0.1m, sec.Quantity);
        Assert.Equal(120m, sec.UnitPriceRate);   // 12 / 0.1

        var buy = await db.AccountTransactions.SingleAsync(t => t.Id == buyId, CT);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), buy.OccurredAt);

        // The single price row moved to the new rate + date.
        var price = await db.CommodityPrices.SingleAsync(p => p.CreatedByTransactionId == buyId, CT);
        Assert.Equal(120m, price.Rate);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), price.PriceAt);
    }

    [Fact]
    public async Task Patch_Drip_PriceRowCollision_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "CollideDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "CVFV");

        // DRIP 1 at 2026-04-23 -> price row (CVFV, USD) at that instant.
        await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);

        // DRIP 2 at a different instant -> its own price row.
        var drip2Resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 6m, 0.06m, "2026-05-01T12:00:00Z"), CT);
        var drip2 = await drip2Resp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var drip2DividendId = drip2.GetProperty("id").GetGuid();

        // Patch DRIP 2 onto DRIP 1's instant -> (CVFV, USD) price collision -> clean 400.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/drips/{drip2DividendId}",
            DripBody(account, vfv, usd, 6m, 0.06m, "2026-04-23T12:00:00Z"), CT);
        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task Patch_Drip_ChangingSecurity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "ImmutDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "IVFV");
        var other = await CreateSecurityAsync(client, "IXEQT");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var dividendId = created.GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/drips/{dividendId}",
            DripBody(account, other, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task Delete_Drip_Both_RemovesBothHalvesAndPrice()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "DelBothDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "DVFV");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var dividendId = created.GetProperty("id").GetGuid();
        var buyId = created.GetProperty("relatedTransactionId").GetGuid();

        // The dedicated both-halves delete lives under /drips/{id}.
        var delResp = await client.DeleteAsync($"/api/v1/finances/transactions/drips/{dividendId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == dividendId || t.Id == buyId, CT));
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == buyId, CT));
    }

    [Fact]
    public async Task Delete_Drip_OneHalf_GenericDelete_LeavesEditableSurvivor()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "DelOneDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "OVFV");

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var dividendId = created.GetProperty("id").GetGuid();
        var buyId = created.GetProperty("relatedTransactionId").GetGuid();

        // "Delete this one only" = the generic delete on a single half. The Buy survivor's link is nulled.
        var delResp = await client.DeleteAsync($"/api/v1/finances/transactions/{dividendId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == dividendId, CT));
        var buy = await db.AccountTransactions.SingleAsync(t => t.Id == buyId, CT);
        Assert.Null(buy.RelatedTransactionId);

        // The survivor is now a standalone InvestmentBuy and is editable via the trade editor again
        // (the DRIP-half edit guard no longer applies once the link is gone).
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/trades/{buyId}", new
            {
                accountId = account, side = "Buy", securityId = vfv, fundingCurrencyId = usd,
                quantity = 0.05m, unitPrice = 100m, occurredAt = "2026-04-23T12:00:00Z",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
    }

    [Fact]
    public async Task Create_Drip_AfterBalanceEntry_RefiresReconciliation()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var account = await CreateInvestmentAccountAsync(client, usd, "ReconDrip-RRSP");
        var vfv = await CreateSecurityAsync(client, "RVFV");

        // Assert VFV position = 1.0 unit on 2026-05-31 (prior = 0, delta = 1.0).
        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{account}/balance-entries", new
            {
                commodityId = vfv,
                assertedAmount = 1.0m,
                date = "2026-05-31",
            }, CT);
        beResp.EnsureSuccessStatusCode();
        var be = await beResp.Content.ReadFromJsonAsync<JsonElement>(CT);
        var beId = be.GetProperty("transactionId").GetGuid();

        // DRIP before the BE adds +0.05 VFV, shifting the BE's prior to 0.05, so reconciliation must
        // drop the BE's account-leg delta to 0.95.
        var dripResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(account, vfv, usd, 5m, 0.05m, "2026-05-01T12:00:00Z"), CT);
        Assert.Equal(HttpStatusCode.Created, dripResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beAccountSplit = await db.AccountTransactionSplits
            .SingleAsync(s => s.TransactionId == beId && s.AccountId == account, CT);
        Assert.Equal(0.95m, beAccountSplit.Quantity);
    }

    [Fact]
    public async Task Create_Drip_OnNonInvestmentAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var vfv = await CreateSecurityAsync(client, "NVFV");

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing-Drip",
            kind = "Cash",
            reportingCurrencyId = usd,
        }, CT);
        var cash = await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/drips",
            DripBody(cash!.Id, vfv, usd, 5m, 0.05m, "2026-04-23T12:00:00Z"), CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
