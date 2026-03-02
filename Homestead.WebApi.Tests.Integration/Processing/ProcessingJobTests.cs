using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Processing;

[Collection("Integration")]
public class ProcessingJobTests : IntegrationTestBase
{
    public ProcessingJobTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetProcessingJobs_ReturnsList()
    {
        var client = await CreateAdminClientAsync();

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("job-test.pdf"), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", "job-test.pdf");

        var uploadResponse = await client.PostAsync("/api/v1/documents", form, CT);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var jobBody = await uploadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(jobBody!.JobId);

        var response = await client.GetAsync("/api/v1/processing/jobs?Page=1&PageSize=10", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ProcessingJobItem>>(CT);
        body!.Items.Should().NotBeEmpty();
        body.Items.Should().Contain(j => j.Id == jobBody.JobId);
    }

    [Fact]
    public async Task GetProcessingJobs_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/processing/jobs?Page=1&PageSize=10", CT);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DocumentUpload_CreatesProcessingJob()
    {
        var client = await CreateAdminClientAsync();

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("processing-test.pdf"), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", "processing-test.pdf");

        var uploadResponse = await client.PostAsync("/api/v1/documents", form, CT);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var jobBody = await uploadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        jobBody!.JobId.Should().NotBeEmpty();

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Homestead.WebApi.Persistence.AppDbContext>();
        var job = await db.ProcessingJobs.FindAsync([jobBody.JobId], CT);
        job.Should().NotBeNull();
        job!.Type.Should().Be(Homestead.WebApi.Domains.Processing.ProcessingJobType.UploadDocument);
    }

    private record ProcessingJobItem(Guid Id, string Type, string Status, Guid? EntityId, string? Error);
}
