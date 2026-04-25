using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class TransfersEndpointTests : IntegrationTestBase
{
    public TransfersEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetCurrencyIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateCashAccountAsync(HttpClient client, Guid currencyId, string name,
        decimal initial = 0m)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Cash",
            reportingCurrencyId = currencyId,
            initialBalance = new { amount = initial, asOfDate = "2026-01-01" },
        }, CT);
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    [Fact]
    public async Task Create_SameCurrencyTransfer_RoundTrip_NoLinkedPrice()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var chequing = await CreateCashAccountAsync(client, cad, "Chequing", 1000m);
        var savings = await CreateCashAccountAsync(client, cad, "Savings", 500m);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = chequing,
            destinationAccountId = savings,
            sourceAmount = 200m,
            date = "2026-04-23",
            description = "April rent transfer",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TransactionItem>(CT);
        Assert.Equal("Transfer", body!.Shape);
        Assert.Equal("April rent transfer", body.Description);
        Assert.Equal(2, body.Splits.Count);

        // No linked price for same-currency.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priceCount = await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == body.Id, CT);
        Assert.Equal(0, priceCount);

        // GET should return the same transaction.
        var getResp = await client.GetAsync($"/api/v1/finances/transactions/{body.Id}", CT);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    [Fact]
    public async Task Create_CrossCurrencyTransfer_EmitsLinkedPriceRow()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var cad = await GetCurrencyIdAsync("CAD");
        var usdChequing = await CreateCashAccountAsync(client, usd, "USD Chequing", 1000m);
        var cadChequing = await CreateCashAccountAsync(client, cad, "CAD Chequing", 0m);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = usdChequing,
            destinationAccountId = cadChequing,
            sourceAmount = 100m,
            destinationAmount = 135m,
            date = "2026-04-23",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TransactionItem>(CT);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p =>
            p.CreatedByTransactionId == body!.Id, CT);
        Assert.Equal(usd, price.CommodityId);
        Assert.Equal(cad, price.QuoteCurrencyId);
        Assert.Equal(1.35m, price.Rate);
    }

    [Fact]
    public async Task Patch_Transfer_UpdatesAmountAndLinkedPrice()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var cad = await GetCurrencyIdAsync("CAD");
        var usdAcct = await CreateCashAccountAsync(client, usd, "USD-A", 1000m);
        var cadAcct = await CreateCashAccountAsync(client, cad, "CAD-A", 0m);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = usdAcct,
            destinationAccountId = cadAcct,
            sourceAmount = 100m,
            destinationAmount = 135m,
            date = "2026-04-23",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<TransactionItem>(CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/transactions/transfers/{created!.Id}", new
            {
                sourceAccountId = usdAcct,
                destinationAccountId = cadAcct,
                sourceAmount = 200m,
                destinationAmount = 270m,
                date = "2026-04-23",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p =>
            p.CreatedByTransactionId == created.Id, CT);
        // 270 / 200 = 1.35 — same rate, so same price row.
        Assert.Equal(1.35m, price.Rate);
    }

    [Fact]
    public async Task Delete_Transfer_RemovesTransactionAndCascadesLinkedPrice()
    {
        var client = await CreateAdminClientAsync();
        var usd = await GetCurrencyIdAsync("USD");
        var cad = await GetCurrencyIdAsync("CAD");
        var usdAcct = await CreateCashAccountAsync(client, usd, "USD-D", 1000m);
        var cadAcct = await CreateCashAccountAsync(client, cad, "CAD-D", 0m);

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = usdAcct,
            destinationAccountId = cadAcct,
            sourceAmount = 100m,
            destinationAmount = 135m,
            date = "2026-04-23",
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<TransactionItem>(CT);

        var deleteResp = await client.DeleteAsync($"/api/v1/finances/transactions/{created!.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.AccountTransactions.CountAsync(t => t.Id == created.Id, CT));
        Assert.Equal(0, await db.CommodityPrices.CountAsync(p => p.CreatedByTransactionId == created.Id, CT));
    }

    [Fact]
    public async Task Create_Transfer_SourceEqualsDestination_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var account = await CreateCashAccountAsync(client, cad, "OnlyOne", 100m);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = account,
            destinationAccountId = account,
            sourceAmount = 50m,
            date = "2026-04-23",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Transfer_NegativeAmount_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var cad = await GetCurrencyIdAsync("CAD");
        var a = await CreateCashAccountAsync(client, cad, "A1", 100m);
        var b = await CreateCashAccountAsync(client, cad, "B1", 100m);

        var resp = await client.PostAsJsonAsync("/api/v1/finances/transactions/transfers", new
        {
            sourceAccountId = a,
            destinationAccountId = b,
            sourceAmount = -50m,
            date = "2026-04-23",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

internal record TransactionItem(
    Guid Id, string Shape, DateTimeOffset OccurredAt, string Description,
    Guid? RelatedTransactionId,
    IReadOnlyList<TransactionSplitItem> Splits);

internal record TransactionSplitItem(
    Guid Id, Guid AccountId, Guid CommodityId, decimal Quantity,
    decimal? UnitPriceRate, Guid? UnitPriceCommodityId, string? Memo);
