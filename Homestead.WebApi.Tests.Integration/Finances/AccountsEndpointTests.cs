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
    public async Task GetAccounts_ListsSystemAccounts()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=100&includeArchived=true", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);
        Assert.Contains(body!.Items, a => a.Name == SystemAccountSeeder.OpeningBalancesName && a.IsSystem);
        Assert.Contains(body.Items, a => a.Name == SystemAccountSeeder.AdjustmentsName && a.IsSystem);
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
        Assert.Equal(1000m, splits[0].Value);
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
        Assert.Equal(-500m, split.Value);
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

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{created!.Id}", CT);

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

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{created!.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
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
