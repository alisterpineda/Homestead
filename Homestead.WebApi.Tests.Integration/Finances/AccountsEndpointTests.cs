using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class AccountsEndpointTests : IntegrationTestBase
{
    public AccountsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetUsdIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
    }

    private static object CashInitialBalance(decimal amount = 0m) => new
    {
        amount,
        asOfDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
    };

    [Fact]
    public async Task GetAccounts_ExcludesSystemAccounts()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        // One user account so the list (and totalCount) has a known non-system baseline.
        await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Visible-Cash",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);

        // includeArchived=true proves the system-account exclusion is unconditional.
        var response = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=100&includeArchived=true", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);
        Assert.DoesNotContain(body!.Items, a => a.Name == SystemAccountSeeder.OpeningBalancesName);
        Assert.DoesNotContain(body.Items, a => a.Name == SystemAccountSeeder.AdjustmentsName);
        Assert.DoesNotContain(body.Items, a => a.Name == SystemAccountSeeder.IncomeName);
        Assert.DoesNotContain(body.Items, a => a.Name == SystemAccountSeeder.ExpenseName);
        Assert.DoesNotContain(body.Items, a => a.IsSystem);

        // totalCount is counted from the same filtered query — only the user account remains.
        Assert.Equal(1, body.TotalCount);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task GetAccount_SystemAccount_Returns404()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemAccountId = await db.Accounts
            .Where(a => a.IsSystem)
            .Select(a => a.Id)
            .FirstAsync(CT);

        var response = await client.GetAsync($"/api/v1/finances/accounts/{systemAccountId}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_Cash_WithZeroInitialBalance_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = usdId,
            institution = "Big Bank",
            initialBalance = CashInitialBalance(0m),
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal("Chequing", body!.Name);
        Assert.Equal("Cash", body.Kind);
        Assert.Equal("Asset", body.AccountType);
        Assert.Equal(usdId, body.ReportingCurrencyId);
        Assert.False(body.IsSystem);

        // Zero-amount opening balance is skipped — no transaction row.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splitCount = await db.AccountTransactionSplits.CountAsync(s => s.AccountId == body.Id, CT);
        Assert.Equal(0, splitCount);
    }

    [Fact]
    public async Task CreateAccount_Cash_WithPositiveInitialBalance_EmitsOpeningBalance()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(1000m),
        }, CT);

        var body = await response.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var splits = await db.AccountTransactionSplits
            .Where(s => s.AccountId == body!.Id)
            .ToListAsync(CT);
        Assert.Single(splits);
        Assert.Equal(1000m, splits[0].Quantity);
    }

    [Fact]
    public async Task CreateAccount_CreditCard_WithPositiveInitialBalance_StoresNegativeValue()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Visa",
            kind = "CreditCard",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(500m),
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal("Liability", body!.AccountType);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var split = await db.AccountTransactionSplits.SingleAsync(s => s.AccountId == body.Id, CT);
        Assert.Equal(-500m, split.Quantity);
    }

    [Fact]
    public async Task CreateAccount_Investment_DerivesAsset()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "RRSP",
            kind = "Investment",
            reportingCurrencyId = usdId,
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal("Investment", body!.Kind);
        Assert.Equal("Asset", body.AccountType);
    }

    [Fact]
    public async Task CreateAccount_Investment_WithInitialBalance_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "RRSP",
            kind = "Investment",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(1000m),
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_NonInvestment_MissingInitialBalance_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = usdId,
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_MissingReportingCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Bad",
            kind = "Cash",
            reportingCurrencyId = Guid.Empty,
            initialBalance = CashInitialBalance(0m),
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_KindOutOfEnumRange_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Garbage",
            kind = 99,
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_SecurityAsReportingCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var security = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "TEST-SEC",
            Exchange = "OTHER",
            Name = "Test Security",
            Kind = CommodityKind.Security,
            DisplayPrecision = 2,
        };
        db.Commodities.Add(security);
        await db.SaveChangesAsync(CT);

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Bad",
            kind = "Cash",
            reportingCurrencyId = security.Id,
            initialBalance = CashInitialBalance(0m),
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchAccount_UpdatesName_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Original",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/accounts/{created!.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Renamed" } });

        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal("Renamed", patched!.Name);
    }

    [Fact]
    public async Task PatchAccount_SystemAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemAccountId = await db.Accounts
            .Where(a => a.IsSystem)
            .Select(a => a.Id)
            .FirstAsync(CT);

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/finances/accounts/{systemAccountId}",
            new[] { new { op = "replace", path = "/name", value = (object)"Hijacked" } });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_SystemAccount_Returns400()
    {
        var client = await CreateAdminClientAsync();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemAccountId = await db.Accounts
            .Where(a => a.IsSystem)
            .Select(a => a.Id)
            .FirstAsync(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{systemAccountId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithZeroInitialBalance_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Disposable",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.False(created!.HasTransactions);

        var getResp = await client.GetAsync($"/api/v1/finances/accounts/{created.Id}", CT);
        var fetched = await getResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.False(fetched!.HasTransactions);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{created.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithOpeningBalance_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Has-Money",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(100m),
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.True(created!.HasTransactions);

        var getResp = await client.GetAsync($"/api/v1/finances/accounts/{created.Id}", CT);
        var fetched = await getResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.True(fetched!.HasTransactions);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{created.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_PopulatesLatestTransactionDate()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var withTxResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Has-Tx",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(250m),
        }, CT);
        var withTx = await withTxResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var emptyResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "No-Tx",
            kind = "Cash",
            reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);
        var empty = await emptyResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var listResp = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=200&includeArchived=true", CT);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);

        var withTxItem = list!.Items.Single(a => a.Id == withTx!.Id);
        Assert.NotNull(withTxItem.LatestTransactionDate);

        var emptyItem = list.Items.Single(a => a.Id == empty!.Id);
        Assert.Null(emptyItem.LatestTransactionDate);

        // Look up the actual transaction date from the DB and confirm the response matches.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedDate = await db.AccountTransactionSplits
            .Where(s => s.AccountId == withTx!.Id)
            .Select(s => s.Transaction.OccurredAt)
            .MaxAsync(CT);
        Assert.Equal(expectedDate, withTxItem.LatestTransactionDate);
    }

    [Fact]
    public async Task GetAccounts_FilterByKind_WorksForUserAccounts()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Cash-A", kind = "Cash", reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);
        await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "CC-A", kind = "CreditCard", reportingCurrencyId = usdId,
            initialBalance = CashInitialBalance(0m),
        }, CT);

        var response = await client.GetAsync("/api/v1/finances/accounts?kind=Cash&page=1&pageSize=100", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);

        Assert.All(body!.Items, a => Assert.Equal("Cash", a.Kind));
        Assert.Contains(body.Items, a => a.Name == "Cash-A");
        Assert.DoesNotContain(body.Items, a => a.Name == "CC-A");
    }
}
