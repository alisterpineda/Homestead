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
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        var jobBody = await uploadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(jobBody!.JobId);

        var response = await client.GetAsync("/api/v1/processing/jobs?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ProcessingJobItem>>(CT);
        Assert.NotEmpty(body!.Items);
        Assert.Contains(body.Items, j => j.Id == jobBody.JobId);
    }

    [Fact]
    public async Task GetProcessingJobs_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/processing/jobs?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        var jobBody = await uploadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, jobBody!.JobId);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Homestead.WebApi.Persistence.AppDbContext>();
        var job = await db.ProcessingJobs.FindAsync([jobBody.JobId], CT);
        Assert.NotNull(job);
        Assert.Equal(Homestead.WebApi.Domains.Processing.ProcessingJobType.UploadDocument, job!.Type);
    }

    private record ProcessingJobItem(Guid Id, string Type, string Status, Guid? EntityId, string? Error);
}
