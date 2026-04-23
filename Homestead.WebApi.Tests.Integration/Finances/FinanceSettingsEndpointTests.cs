using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class FinanceSettingsEndpointTests : IntegrationTestBase
{
    public FinanceSettingsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> GetUsdIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == "USD").Select(c => c.Id).SingleAsync(CT);
    }

    [Fact]
    public async Task GetFinanceSettings_ReturnsEmpty_WhenNoRowExists()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/finances/settings", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<FinanceSettingsItem>(CT);
        Assert.Null(body!.DisplayCurrencyId);
    }

    [Fact]
    public async Task PatchFinanceSettings_SetDisplayCurrency_PersistsAndRoundTrips()
    {
        var client = await CreateAdminClientAsync();
        var usdId = await GetUsdIdAsync();

        var patchResp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)usdId.ToString() }
        });
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        var getResp = await client.GetAsync("/api/v1/finances/settings", CT);
        var body = await getResp.Content.ReadFromJsonAsync<FinanceSettingsItem>(CT);
        Assert.Equal(usdId, body!.DisplayCurrencyId);
    }

    [Fact]
    public async Task PatchFinanceSettings_NonCurrencyId_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var securityResp = await client.PostAsJsonAsync("/api/v1/finances/commodities", new
        {
            symbol = "VEQT",
            name = "Vanguard All-Equity",
            displayPrecision = 4
        }, CT);
        var security = await securityResp.Content.ReadFromJsonAsync<FinanceCommodityItem>(CT);

        var patchResp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)security!.Id.ToString() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    [Fact]
    public async Task PatchFinanceSettings_NonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var patchResp = await SendJsonPatchAsync(client, "/api/v1/finances/settings",
            Array.Empty<object>());

        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
    }
}
