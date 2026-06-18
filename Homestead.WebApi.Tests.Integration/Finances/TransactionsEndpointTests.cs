using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class TransactionsEndpointTests : IntegrationTestBase
{
    public TransactionsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetUsdIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> GetOpeningBalancesSystemIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Accounts
            .Where(a => a.IsSystem && a.Name == SystemAccountSeeder.OpeningBalancesName)
            .Select(a => a.Id)
            .SingleAsync(CT);
    }

    // Creates a cash account with a positive opening balance, which posts an opening-balance
    // transaction whose counterpart leg sits on the system Opening-Balances account. That gives
    // the system account a real transaction to over-return if the visibility guard were missing.
    private async Task SeedTransactionTouchingSystemAccountAsync(HttpClient client, Guid usdId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-Tx-Sys",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task GetAccountTransactions_SystemAccount_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        await SeedTransactionTouchingSystemAccountAsync(client, usdId);
        var openingBalancesId = await GetOpeningBalancesSystemIdAsync();

        // Without the guard this returns 200 with the opening-balance transaction (the system
        // account's counterpart leg); the guard makes the system id a consistent 404.
        var response = await client.GetAsync(
            $"/api/v1/finances/accounts/{openingBalancesId}/transactions?page=1&pageSize=25", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransactions_FilterBySystemAccountId_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        await SeedTransactionTouchingSystemAccountAsync(client, usdId);
        var openingBalancesId = await GetOpeningBalancesSystemIdAsync();

        // Without the guard, ?accountId={systemId} over-returns every transaction whose splits
        // touch the system account; the guard 404s consistently with the per-account route.
        var response = await client.GetAsync(
            $"/api/v1/finances/transactions?accountId={openingBalancesId}&page=1&pageSize=25", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
