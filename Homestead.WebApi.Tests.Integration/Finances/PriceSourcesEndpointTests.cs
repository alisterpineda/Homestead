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

    private async Task EnsureSettingsRowAsync(CommodityPriceSource source, bool isEnabled)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.CommodityPriceSourceSettings.FirstOrDefaultAsync(s => s.Source == source, CT);
        if (existing is null)
        {
            db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
            {
                Source = source,
                IsEnabled = isEnabled,
            });
        }
        else
        {
            existing.IsEnabled = isEnabled;
        }
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
    public async Task GetPriceSource_External_ReturnsConfigSchemaAndSupportedPairs()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: false);
        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync("/api/v1/finances/settings/price-sources/BankOfCanada", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DetailResponse>(CT);
        Assert.NotNull(body);
        Assert.Equal("BankOfCanada", body!.Source);
        Assert.NotEmpty(body.SupportedPairs);
        Assert.NotNull(body.ConfigSchema);
    }

    [Fact]
    public async Task PatchPriceSource_SystemSource_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            "/api/v1/finances/settings/price-sources/Manual",
            new { isEnabled = true },
            CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchPriceSource_SetIsEnabled_Persists()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: false);
        var client = await CreateAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada",
            new { isEnabled = true },
            CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.CommodityPriceSourceSettings
            .FirstAsync(s => s.Source == CommodityPriceSource.BankOfCanada, CT);
        Assert.True(settings.IsEnabled);
    }

    [Fact]
    public async Task UpsertSchedule_CreatesNewSchedule()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: true);
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
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: true);
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
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: true);
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
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: false);
        var client = await CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync(
            "/api/v1/finances/settings/price-sources/BankOfCanada/runs",
            new { },
            CT);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RunNow_EnabledSource_EnqueuesJob()
    {
        await EnsureSettingsRowAsync(CommodityPriceSource.BankOfCanada, isEnabled: true);
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
        int PriceCount,
        DateTimeOffset? LastImportAt,
        string? LastImportStatus);

    private record DetailResponse(
        string Source,
        string DisplayName,
        string Kind,
        bool IsEnabled,
        string? Config,
        IReadOnlyList<string>? PairAllowList,
        DateTimeOffset? LastImportAt,
        string? LastImportStatus,
        string? LastImportMessage,
        ConfigSchema ConfigSchema,
        IReadOnlyList<SupportedPair> SupportedPairs,
        ScheduleSummary? Schedule,
        int PriceCount);

    private record ConfigSchema(IReadOnlyList<ConfigField> Fields);
    private record ConfigField(string Key, string Label, string Type, bool IsRequired, string? Help, string? DefaultValue);
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
