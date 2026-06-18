using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class OpeningBalancesEndpointTests : IntegrationTestBase
{
    public OpeningBalancesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetUsdIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
    }

    private async Task<Guid> CreateInvestmentAccountAsync(HttpClient client, Guid usdId, string name = "RRSP")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name,
            kind = "Investment",
            reportingCurrencyId = usdId,
        }, CT);
        var body = await resp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        return body!.Id;
    }

    private async Task<Commodity> AddSecurityAsync(string symbol = "VEQT", string exchange = "TSX")
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Commodities
            .FirstOrDefaultAsync(c => c.Symbol == symbol && c.Exchange == exchange, CT);
        if (existing is not null) return existing;

        var security = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Exchange = exchange,
            Name = symbol,
            Kind = CommodityKind.Security,
            DisplayPrecision = 4,
        };
        db.Commodities.Add(security);
        await db.SaveChangesAsync(CT);
        return security;
    }

    [Fact]
    public async Task AddCashPosition_ToInvestment_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new
            {
                commodityId = usdId,
                quantity = 5000m,
                asOfDate = "2026-04-23",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(5000m, body!.Quantity);
        Assert.Null(body.ValuePerUnit);
    }

    [Fact]
    public async Task AddSecurityPosition_EmitsPriceAndBalancesInReportingCurrency()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Brokerage");
        var veqt = await AddSecurityAsync("VEQT");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new
            {
                commodityId = veqt.Id,
                quantity = 100m,
                valuePerUnit = 35.12m,
                asOfDate = "2026-04-23",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(100m, body!.Quantity);
        Assert.Equal(35.12m, body.ValuePerUnit);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var price = await db.CommodityPrices.SingleAsync(p =>
            p.CommodityId == veqt.Id && p.QuoteCurrencyId == usdId, CT);
        Assert.Equal(35.12m, price.Rate);
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero), price.PriceAt);
        Assert.NotNull(price.CreatedByTransactionId);
        Assert.Equal(body.TransactionId, price.CreatedByTransactionId);
    }

    [Fact]
    public async Task DuplicateAdd_ForSameAccountCommodity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Dup-acct");

        var first = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 500m, asOfDate = "2026-04-24" }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task AddToNonInvestmentAccount_WithForeignCommodity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var cash = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-USD",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 0m, asOfDate = "2026-04-23" },
        }, CT);
        var cashBody = await cash.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        // Set up a different currency commodity.
        Commodity cad;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cad = await db.Commodities.FirstAsync(c => c.Symbol == "CAD", CT);
        }

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cashBody!.Id}/opening-balances",
            new { commodityId = cad.Id, quantity = 500m, asOfDate = "2026-04-23" }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetList_ReturnsAllPositionsForAccount()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Multi-acct");
        var veqt = await AddSecurityAsync("VEQT-LIST");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);
        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = veqt.Id, quantity = 10m, valuePerUnit = 50m, asOfDate = "2026-04-23" }, CT);

        var resp = await client.GetAsync($"/api/v1/finances/accounts/{accountId}/opening-balances", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var items = await resp.Content.ReadFromJsonAsync<List<OpeningBalanceItem>>(CT);
        Assert.Equal(2, items!.Count);
    }

    [Fact]
    public async Task GetSingle_ReturnsOpeningBalance()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Single-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 2000m, asOfDate = "2026-04-23" }, CT);

        var resp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(2000m, body!.Quantity);
    }

    [Fact]
    public async Task GetSingle_NotFound_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Empty-acct");

        var resp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Patch_ChangesQuantity()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Patch-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}",
            new { quantity = 1500m, asOfDate = "2026-04-23" }, CT);

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal(1500m, body!.Quantity);
    }

    [Fact]
    public async Task Delete_RemovesOpeningBalance()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Del-acct");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var delResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var getResp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task Delete_WithDownstreamBalanceEntry_ReconcilesAgainstEquityAdjustments()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-Reconcile",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        Assert.Equal(HttpStatusCode.Created, cashResp.StatusCode);
        var cash = (await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;

        var balanceResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/balance-entries",
            new { commodityId = usdId, assertedAmount = 1500m, date = "2026-02-15" }, CT);
        Assert.Equal(HttpStatusCode.Created, balanceResp.StatusCode);

        var beforeGet = await client.GetAsync($"/api/v1/finances/accounts/{cash.Id}", CT);
        var before = (await beforeGet.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
        Assert.Equal(1500m, before.NativeBalance);

        var delResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{cash.Id}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var afterGet = await client.GetAsync($"/api/v1/finances/accounts/{cash.Id}", CT);
        var after = (await afterGet.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
        Assert.Equal(1500m, after.NativeBalance);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var openingBalanceCount = await db.AccountTransactions
            .OfType<OpeningBalanceTransaction>()
            .CountAsync(t => t.Splits.Any(s => s.AccountId == cash.Id), CT);
        Assert.Equal(0, openingBalanceCount);

        var adjustments = await db.Accounts.SingleAsync(
            a => a.IsSystem && a.Name == SystemAccountSeeder.AdjustmentsName, CT);
        var adjustmentsBalance = await db.AccountTransactionSplits
            .Where(s => s.AccountId == adjustments.Id && s.CommodityId == usdId)
            .SumAsync(s => s.Quantity, CT);
        Assert.Equal(-1500m, adjustmentsBalance);
    }

    [Fact]
    public async Task CreateOpeningBalance_BeforeExistingBalanceEntry_ReconcilesDownstreamDelta()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        // Cash account starting at 0 (amount = 0 ⇒ no auto opening balance is created).
        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-CreateOB-Reconcile",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 0m, asOfDate = "2026-01-01" },
        }, CT);
        Assert.Equal(HttpStatusCode.Created, cashResp.StatusCode);
        var cash = (await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;

        // Balance entry asserts $1500 with no prior activity ⇒ delta = 1500.
        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/balance-entries",
            new { commodityId = usdId, assertedAmount = 1500m, date = "2026-02-15" }, CT);
        Assert.Equal(HttpStatusCode.Created, beResp.StatusCode);

        // Insert an opening balance dated EARLIER than the balance entry. The BE's prior shifts from
        // $0 to $1000, so its delta must be recomputed to $500 and Equity:Adjustments must absorb the
        // change — otherwise (pre-fix) the account balance double-counts to $2500.
        var obResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-01-01" }, CT);
        Assert.Equal(HttpStatusCode.Created, obResp.StatusCode);

        var afterGet = await client.GetAsync($"/api/v1/finances/accounts/{cash.Id}", CT);
        var after = (await afterGet.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
        Assert.Equal(1500m, after.NativeBalance);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Downstream balance entry's delta recomputed to 1500 - 1000 = 500.
        var beAccountSplit = await db.AccountTransactionSplits
            .Where(s => s.AccountId == cash.Id && s.CommodityId == usdId
                     && s.Transaction is BalanceEntryTransaction)
            .Select(s => s.Quantity)
            .SingleAsync(CT);
        Assert.Equal(500m, beAccountSplit);

        var adjustments = await db.Accounts.SingleAsync(
            a => a.IsSystem && a.Name == SystemAccountSeeder.AdjustmentsName, CT);
        var adjustmentsBalance = await db.AccountTransactionSplits
            .Where(s => s.AccountId == adjustments.Id && s.CommodityId == usdId)
            .SumAsync(s => s.Quantity, CT);
        Assert.Equal(-500m, adjustmentsBalance);
    }

    [Fact]
    public async Task PatchOpeningBalance_ChangesQuantity_ReconcilesDownstreamDelta()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        // Cash account with an auto opening balance of $1000 at 2026-01-01.
        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-PatchOB-Reconcile",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        Assert.Equal(HttpStatusCode.Created, cashResp.StatusCode);
        var cash = (await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;

        // BE asserts $1500: prior = $1000 ⇒ delta = $500.
        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/balance-entries",
            new { commodityId = usdId, assertedAmount = 1500m, date = "2026-02-15" }, CT);
        Assert.Equal(HttpStatusCode.Created, beResp.StatusCode);

        // Raise the opening balance to $1200. The BE's prior becomes $1200 ⇒ delta = $300; the
        // account assertion of $1500 must hold. Pre-fix the edit skipped reconciliation, leaving the
        // BE delta at $500 and the balance at $1700.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/opening-balances/{usdId}",
            new { quantity = 1200m, asOfDate = "2026-01-01" }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        var afterGet = await client.GetAsync($"/api/v1/finances/accounts/{cash.Id}", CT);
        var after = (await afterGet.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
        Assert.Equal(1500m, after.NativeBalance);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beAccountSplit = await db.AccountTransactionSplits
            .Where(s => s.AccountId == cash.Id && s.CommodityId == usdId
                     && s.Transaction is BalanceEntryTransaction)
            .Select(s => s.Quantity)
            .SingleAsync(CT);
        Assert.Equal(300m, beAccountSplit);
    }

    [Fact]
    public async Task PatchOpeningBalance_ChangesQuantityAndDate_ReconcilesDownstreamDelta()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var cashResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-PatchOBDate-Reconcile",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        Assert.Equal(HttpStatusCode.Created, cashResp.StatusCode);
        var cash = (await cashResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;

        var beResp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/balance-entries",
            new { commodityId = usdId, assertedAmount = 1500m, date = "2026-02-15" }, CT);
        Assert.Equal(HttpStatusCode.Created, beResp.StatusCode);

        // Move the OB to a different (still earliest) date AND change its quantity. This exercises the
        // touched-pairs-covering-both-instants path in UpdateAsync; the downstream BE must re-reconcile.
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{cash.Id}/opening-balances/{usdId}",
            new { quantity = 1200m, asOfDate = "2026-01-10" }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        var afterGet = await client.GetAsync($"/api/v1/finances/accounts/{cash.Id}", CT);
        var after = (await afterGet.Content.ReadFromJsonAsync<FinanceAccountItem>(CT))!;
        Assert.Equal(1500m, after.NativeBalance);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beAccountSplit = await db.AccountTransactionSplits
            .Where(s => s.AccountId == cash.Id && s.CommodityId == usdId
                     && s.Transaction is BalanceEntryTransaction)
            .Select(s => s.Quantity)
            .SingleAsync(CT);
        Assert.Equal(300m, beAccountSplit);
    }

    [Fact]
    public async Task Create_WithMemo_RoundTripsThroughResponse()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Desc-create");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new
            {
                commodityId = usdId,
                quantity = 1000m,
                asOfDate = "2026-04-23",
                memo = "Imported from old tracker",
            }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal("Imported from old tracker", body!.Memo);
    }

    [Fact]
    public async Task Create_BlankMemo_PersistsEmptyMemo()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Desc-default");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal("", body!.Memo);
    }

    [Fact]
    public async Task Patch_UpdatesMemo()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Desc-patch");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}",
            new
            {
                quantity = 1000m,
                asOfDate = "2026-04-23",
                memo = "Edited via PATCH",
            }, CT);

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var body = await patchResp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal("Edited via PATCH", body!.Memo);
    }

    // Discriminator regression: before the TPH refactor, opening balances were identified by
    // the literal memo string "Opening balance". Editing the memo to anything
    // else would have broken Get / Patch / Delete and made the row vanish from shape filters.
    // This test pins the new behavior where TPH (Type column / OfType<OpeningBalanceTransaction>)
    // is the structural identifier.
    [Fact]
    public async Task EditMemo_DoesNotBreakDiscriminatorLookups()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();
        var accountId = await CreateInvestmentAccountAsync(client, usdId, "Desc-discriminator");

        await client.PostAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances",
            new { commodityId = usdId, quantity = 1000m, asOfDate = "2026-04-23" }, CT);

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}",
            new
            {
                quantity = 1000m,
                asOfDate = "2026-04-23",
                memo = "Renamed to something arbitrary",
            }, CT);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        // The single-position GET still locates the row.
        var getResp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var got = await getResp.Content.ReadFromJsonAsync<OpeningBalanceItem>(CT);
        Assert.Equal("Renamed to something arbitrary", got!.Memo);

        // Listing the account's opening balances still includes the renamed row.
        var listResp = await client.GetAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances", CT);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var items = await listResp.Content.ReadFromJsonAsync<List<OpeningBalanceItem>>(CT);
        Assert.Single(items!);
        Assert.Equal("Renamed to something arbitrary", items![0].Memo);

        // Filtering the generic transactions endpoint by TransactionShape.OpeningBalance still
        // returns it — proving the shape filter is type-driven, not memo-driven.
        var shapeResp = await client.GetAsync(
            $"/api/v1/finances/transactions?accountId={accountId}&shape=OpeningBalance&page=1&pageSize=25", CT);
        Assert.Equal(HttpStatusCode.OK, shapeResp.StatusCode);
        var shapeBody = await shapeResp.Content.ReadFromJsonAsync<TransactionPage>(CT);
        Assert.Equal(1, shapeBody!.TotalCount);
        Assert.Equal("Renamed to something arbitrary", shapeBody.Items[0].Memo);
        Assert.Equal("OpeningBalance", shapeBody.Items[0].Shape);

        // Delete still finds the row by (account, commodity).
        var delResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{accountId}/opening-balances/{usdId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
    }

    private record OpeningBalanceItem(
        Guid TransactionId, Guid AccountId, Guid CommodityId, string Memo,
        decimal Quantity, decimal? ValuePerUnit, DateOnly AsOfDate);

    private record TransactionPage(int Page, int PageSize, int TotalCount, List<TransactionItem> Items);

    private record TransactionItem(Guid Id, string Shape, string Memo);
}
