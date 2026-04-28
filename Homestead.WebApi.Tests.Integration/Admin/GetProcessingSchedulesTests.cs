using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Admin;

[Collection("Integration")]
public class GetProcessingSchedulesTests : IntegrationTestBase
{
    public GetProcessingSchedulesTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetSchedules_AsAdmin_ReturnsList()
    {
        await SeedScheduleAsync("Alpha");
        await SeedScheduleAsync("Bravo");

        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync("/api/v1/admin/schedules?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ScheduleItem>>(CT);
        Assert.NotNull(body);
        Assert.True(body!.TotalCount >= 2);
        Assert.Contains(body.Items, s => s.Name == "Alpha");
        Assert.Contains(body.Items, s => s.Name == "Bravo");
    }

    [Fact]
    public async Task GetSchedules_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();
        var response = await client.GetAsync("/api/v1/admin/schedules?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedules_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/admin/schedules?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedules_FilterByIsEnabled_NarrowsResults()
    {
        await SeedScheduleAsync("EnabledOne", isEnabled: true);
        await SeedScheduleAsync("DisabledOne", isEnabled: false);

        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync(
            "/api/v1/admin/schedules?Page=1&PageSize=50&IsEnabled=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ScheduleItem>>(CT);
        Assert.All(body!.Items, s => Assert.False(s.IsEnabled));
        Assert.Contains(body.Items, s => s.Name == "DisabledOne");
        Assert.DoesNotContain(body.Items, s => s.Name == "EnabledOne");
    }

    [Fact]
    public async Task GetSchedules_FilterByJobType_NarrowsResults()
    {
        await SeedScheduleAsync("UploadOne", jobType: ProcessingJobType.UploadDocument);
        await SeedScheduleAsync("DownloadOne", jobType: ProcessingJobType.DownloadDocuments);

        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync(
            $"/api/v1/admin/schedules?Page=1&PageSize=50&JobType={ProcessingJobType.DownloadDocuments}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ScheduleItem>>(CT);
        Assert.All(body!.Items, s => Assert.Equal("DownloadDocuments", s.JobType));
        Assert.Contains(body.Items, s => s.Name == "DownloadOne");
        Assert.DoesNotContain(body.Items, s => s.Name == "UploadOne");
    }

    [Fact]
    public async Task GetSchedules_Pagination_RespectsPageSize()
    {
        for (var i = 0; i < 5; i++)
            await SeedScheduleAsync($"Page-{i:D2}");

        var client = await CreateAdminClientAsync();
        var response = await client.GetAsync(
            "/api/v1/admin/schedules?Page=1&PageSize=2", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ScheduleItem>>(CT);
        Assert.Equal(2, body!.Items.Count);
        Assert.Equal(2, body.PageSize);
    }

    private record ScheduleItem(
        Guid Id,
        string Name,
        string? Description,
        string JobType,
        int Priority,
        string CronExpression,
        string TimeZone,
        bool IsEnabled,
        Guid? CreatedByUserId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastRunAt,
        DateTimeOffset NextRunAt,
        Guid? LastJobId,
        string? LastRunStatus);
}
