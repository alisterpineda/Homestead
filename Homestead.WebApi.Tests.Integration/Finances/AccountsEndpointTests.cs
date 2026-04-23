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

    [Fact]
    public async Task GetAccounts_ListsSystemAccounts()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/finances/accounts?page=1&pageSize=100", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FinanceAccountItem>>(CT);
        Assert.Contains(body!.Items, a => a.Name == SystemAccountSeeder.OpeningBalancesName && a.IsSystem);
        Assert.Contains(body.Items, a => a.Name == SystemAccountSeeder.AdjustmentsName && a.IsSystem);
    }

    [Fact]
    public async Task CreateAccount_Asset_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            accountType = "Asset",
            defaultCommodityId = usdId,
            isMultiPosition = false,
            institution = "Big Bank"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);
        Assert.Equal("Chequing", body!.Name);
        Assert.False(body.IsSystem);
    }

    [Fact]
    public async Task CreateAccount_IncomeType_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var response = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Salary",
            accountType = "Income",
            defaultCommodityId = usdId
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
            accountType = "Asset",
            defaultCommodityId = usdId,
            isMultiPosition = false
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
    public async Task DeleteAccount_UserAccount_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Disposable",
            accountType = "Asset",
            defaultCommodityId = usdId,
            isMultiPosition = false
        }, CT);
        var created = await createResp.Content.ReadFromJsonAsync<FinanceAccountItem>(CT);

        var deleteResp = await client.DeleteAsync(
            $"/api/v1/finances/accounts/{created!.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }
}
