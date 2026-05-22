using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class NetWorthHistoryEndpointTests : IntegrationTestBase
{
    public NetWorthHistoryEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private record NetWorthHistoryPointDto(DateTimeOffset AsOf, decimal? Value, decimal? Assets, decimal? Liabilities);

    private record NetWorthHistoryResponseDto(
        string Granularity, string Range, DateTimeOffset? From, DateTimeOffset To,
        IReadOnlyList<NetWorthHistoryPointDto> Points, bool HasUnpriced, string? Reason);

    [Fact]
    public async Task GetNetWorthHistory_AsOfWithNonUtcOffset_ReturnsSuccess()
    {
        var client = await CreateAdminClientAsync();

        Guid cadId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cadId = await db.Commodities.Where(c => c.Symbol == "CAD").Select(c => c.Id).SingleAsync(CT);
        }

        var settingsResp = await SendJsonPatchAsync(client, "/api/v1/finances/settings", new[]
        {
            new { op = "replace", path = "/displayCurrencyId", value = (object)cadId.ToString() }
        });
        settingsResp.EnsureSuccessStatusCode();

        var accountResp = await client.PostAsJsonAsync("/api/v1/finances/accounts", new
        {
            name = "Chequing",
            kind = "Cash",
            reportingCurrencyId = cadId,
            initialBalance = new { amount = 1000m, asOfDate = "2026-01-01" },
        }, CT);
        accountResp.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            "/api/v1/finances/reports/net-worth-history?range=ThisMonth&asOf=2026-05-22T23%3A59%3A59.999-06%3A00", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponseDto>(CT);
        Assert.NotNull(body);
        Assert.True(body!.Points.Count > 0);
    }
}
