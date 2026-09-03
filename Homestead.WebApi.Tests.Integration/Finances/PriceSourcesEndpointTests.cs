using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

[Collection("Integration")]
public class PriceSourcesEndpointTests : IntegrationTestBase
{
    public PriceSourcesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // Enablement is config: HomesteadApiFactory pins PriceSources:BankOfCanada:Enabled=true and leaves
    // AlphaVantage unset (disabled + not configured). The settings row only carries product data.
    private async Task EnsureSettingsRowAsync(CommodityPriceSource source)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.CommodityPriceSourceSettings.AnyAsync(s => s.Source == source, CT);
        if (exists) return;

        db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings { Source = source });
        await db.SaveChangesAsync(CT);
    }

    [Fact]
    public async Task ListPriceSources_AsAdmin_IncludesAllEnumValues()
    {
        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync("/api/v1/finances/settings/price-sources", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ListResponse>(CT);
        Assert.NotNull(body);
        Assert.Contains(body!.Items, i => i.Source == "Transaction");
        Assert.Contains(body.Items, i => i.Source == "Manual");
        Assert.Contains(body.Items, i => i.Source == "BankOfCanada");
    }

    [Fact]
    public async Task ListPriceSources_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();
        var response = await client.GetAsync("/api/v1/finances/settings/price-sources", CT);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPriceSource_External_ReturnsProviderReadoutAndSupportedPairs()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync("/api/v1/finances/settings/price-sources/BankOfCanada", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DetailResponse>(CT);
        Assert.NotNull(body);
        Assert.Equal("BankOfCanada", body!.Source);
        Assert.NotEmpty(body.SupportedPairs);
        Assert.True(body.IsEnabled);
        Assert.True(body.IsConfigured);
        Assert.Null(body.ConfigurationError);
        Assert.Null(body.HasApiKey);
        Assert.NotNull(body.BaseUrl);
    }

    [Fact]
    public async Task GetPriceSource_AlphaVantage_Unset_ReportsDisabledAndNotConfigured()
    {
        // No PriceSources:AlphaVantage:* config in the test host → disabled, and invalid (no ApiKey).
        await EnsureSettingsRowAsync(CommodityPriceSource.AlphaVantage);
        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync("/api/v1/finances/settings/price-sources/AlphaVantage", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DetailResponse>(CT);
        Assert.NotNull(body);
        Assert.False(body!.IsEnabled);
        Assert.False(body.IsConfigured);
        Assert.Equal("ApiKey is required.", body.ConfigurationError);
        Assert.False(body.HasApiKey);
        Assert.NotNull(body.BaseUrl);

        var list = await (await client.GetAsync("/api/v1/finances/settings/price-sources", CT))
            .Content.ReadFromJsonAsync<ListResponse>(CT);
        var item = Assert.Single(list!.Items, i => i.Source == "AlphaVantage");
        Assert.False(item.IsEnabled);
        Assert.False(item.IsConfigured);
        Assert.True(Assert.Single(list.Items, i => i.Source == "BankOfCanada").IsConfigured);
    }

    [Fact]
    public async Task PatchPriceSource_SystemSource_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            "/api/v1/finances/settings/price-sources/Manual",
            new { pairAllowList = new[] { "CAD" } },
            CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchPriceSource_PairAllowList_Persists()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        // CAD is seeded as a commodity, so the allow-list resolves.
        var response = await client.PatchAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada",
            new { pairAllowList = new[] { " CAD ", "cad" } },
            CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PatchResponse>(CT);
        Assert.NotNull(body);
        Assert.Equal("BankOfCanada", body!.Source);
        Assert.Equal(new[] { "CAD" }, body.PairAllowList);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.CommodityPriceSourceSettings
            .FirstAsync(s => s.Source == CommodityPriceSource.BankOfCanada, CT);
        Assert.Contains("CAD", settings.PairAllowList);
    }

    [Fact]
    public async Task UpsertSchedule_CreatesNewSchedule()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        var response = await client.PutAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/schedule",
            new { cronExpression = "0 6 * * *", isEnabled = true, priority = 0 },
            CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = await db.ProcessingSchedules
            .Where(s => s.JobType == ProcessingJobType.ImportCommodityPrices)
            .ToListAsync(CT);
        Assert.Single(schedules);
        Assert.Equal("0 6 * * *", schedules[0].CronExpression);
    }

    [Fact]
    public async Task UpsertSchedule_ReplacesExistingForSameSource()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        var first = await client.PutAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/schedule",
            new { cronExpression = "0 6 * * *" }, CT);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.PutAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/schedule",
            new { cronExpression = "0 7 * * *" }, CT);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = await db.ProcessingSchedules
            .Where(s => s.JobType == ProcessingJobType.ImportCommodityPrices)
            .ToListAsync(CT);
        Assert.Single(schedules);
        Assert.Equal("0 7 * * *", schedules[0].CronExpression);
    }

    [Fact]
    public async Task DeleteSchedule_RemovesEntity()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        await client.PutAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/schedule",
            new { cronExpression = "0 6 * * *" }, CT);

        var deleteResp = await client.DeleteAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/schedule", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = await db.ProcessingSchedules
            .Where(s => s.JobType == ProcessingJobType.ImportCommodityPrices)
            .ToListAsync(CT);
        Assert.Empty(schedules);
    }

    [Fact]
    public async Task RunNow_DisabledSource_Returns400()
    {
        // AlphaVantage has no PriceSources config in the test host → disabled.
        await EnsureSettingsRowAsync(CommodityPriceSource.AlphaVantage);
        var client = await CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync(
            "/api/v1/finances/settings/price-sources/AlphaVantage/runs",
            new { },
            CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(CT);
        Assert.Contains("is disabled", problem);
    }

    [Fact]
    public async Task RunNow_NotConfiguredSource_Returns400WithReason()
    {
        // Enabled by config but with no ApiKey: the readout says why, and Run-now refuses with the reason.
        using var factory = Factory.WithWebHostBuilder(b =>
            b.UseSetting("PriceSources:AlphaVantage:Enabled", "true"));
        var client = await AuthHelper.CreateAdminClientAsync(factory);

        var detail = await client.GetFromJsonAsync<DetailResponse>(
            "/api/v1/finances/settings/price-sources/AlphaVantage", CT);
        Assert.True(detail!.IsEnabled);
        Assert.False(detail.IsConfigured);
        Assert.Equal("ApiKey is required.", detail.ConfigurationError);

        var response = await client.PostAsJsonAsync(
            "/api/v1/finances/settings/price-sources/AlphaVantage/runs",
            new { },
            CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(CT);
        Assert.Contains("is not configured: ApiKey is required.", problem);
    }

    [Fact]
    public async Task RunNow_EnabledSource_EnqueuesJob()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada);
        var client = await CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/runs",
            new { },
            CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobIdResponse>(CT);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.JobId);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ProcessingJobs.FirstOrDefaultAsync(j => j.Id == body.JobId, CT);
        Assert.NotNull(job);
        Assert.Equal(ProcessingJobType.ImportCommodityPrices, job!.Type);
    }

    [Fact]
    public async Task AdminSchedules_RejectsImportCommodityPricesCreation()
    {
        var client = await CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Should fail",
            jobType = "ImportCommodityPrices",
            cronExpression = "0 6 * * *",
            isEnabled = true,
        }, CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record ListResponse(IReadOnlyList<ListItem> Items);

    private record ListItem(
        string Source,
        string DisplayName,
        string Kind,
        bool IsEnabled,
        bool IsConfigured,
        int PriceCount,
        DateTimeOffset? LastImportAt,
        string? LastImportStatus);

    private record DetailResponse(
        string Source,
        string DisplayName,
        string Kind,
        bool IsEnabled,
        bool IsConfigured,
        string? ConfigurationError,
        bool? HasApiKey,
        string? BaseUrl,
        IReadOnlyList<string>? PairAllowList,
        DateTimeOffset? LastImportAt,
        string? LastImportStatus,
        string? LastImportMessage,
        IReadOnlyList<SupportedPair> SupportedPairs,
        ScheduleSummary? Schedule,
        int PriceCount);

    private record PatchResponse(string Source, IReadOnlyList<string> PairAllowList);
    private record SupportedPair(string CommoditySymbol, string? Exchange, string QuoteCurrencySymbol);
    private record ScheduleSummary(
        Guid Id,
        string CronExpression,
        bool IsEnabled,
        int Priority,
        DateTimeOffset NextRunAt,
        DateTimeOffset? LastRunAt);

    private record JobIdResponse(Guid JobId);
}
