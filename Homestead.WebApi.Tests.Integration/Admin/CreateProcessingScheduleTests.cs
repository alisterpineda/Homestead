using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Admin;

[Collection("Integration")]
public class CreateProcessingScheduleTests : IntegrationTestBase
{
    public CreateProcessingScheduleTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Post_ValidNoOp_Returns201_AndPersistsRow()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Smoke NoOp",
            description = "Created by integration test",
            jobType = ProcessingJobType.NoOp.ToString(),
            cronExpression = "*/5 * * * *",
            priority = 0,
            isEnabled = true,
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateProcessingScheduleResponse>(CT);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.True(body.NextRunAt > DateTimeOffset.UtcNow,
            $"NextRunAt should be in the future; got {body.NextRunAt:O}");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = await db.ProcessingSchedules.AsNoTracking()
            .FirstAsync(s => s.Id == body.Id, CT);

        Assert.Equal("Smoke NoOp", schedule.Name);
        Assert.Equal("Created by integration test", schedule.Description);
        Assert.Equal(ProcessingJobType.NoOp, schedule.JobType);
        Assert.Equal("*/5 * * * *", schedule.CronExpression);
        Assert.Equal("Etc/UTC", schedule.TimeZone);
        Assert.True(schedule.IsEnabled);
        Assert.NotNull(schedule.CreatedByUserId);
    }

    [Fact]
    public async Task Post_NonNoOpJobType_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Should fail",
            jobType = ProcessingJobType.UploadDocument.ToString(),
            cronExpression = "*/5 * * * *",
            priority = 0,
            isEnabled = true,
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidCron_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Bad cron",
            jobType = ProcessingJobType.NoOp.ToString(),
            cronExpression = "not a cron",
            priority = 0,
            isEnabled = true,
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Forbidden",
            jobType = ProcessingJobType.NoOp.ToString(),
            cronExpression = "*/5 * * * *",
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/admin/schedules", new
        {
            name = "Anon",
            jobType = ProcessingJobType.NoOp.ToString(),
            cronExpression = "*/5 * * * *",
        }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
