using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

[Collection("Integration")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private static byte[]? _cachedPdfBytes;

    private readonly PostgresFixture _fixture;

    protected HomesteadApiFactory Factory => _fixture.Factory;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await _fixture.Respawner.ResetAsync(conn);

        // Clean stored files to avoid duplicate-content errors across test classes
        var documentsDir = Path.Combine(Factory.MediaPath, "documents");
        try { Directory.Delete(documentsDir, recursive: true); } catch (DirectoryNotFoundException) { }
        Directory.CreateDirectory(documentsDir);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected HttpClient CreateAnonymousClient()
    {
        return Factory.CreateClient();
    }

    protected async Task<HttpClient> CreateAdminClientAsync()
    {
        return await AuthHelper.CreateAdminClientAsync(Factory);
    }

    protected async Task<HttpClient> CreateUserClientAsync()
    {
        return await AuthHelper.CreateUserClientAsync(Factory);
    }

    protected IServiceScope CreateScope() => Factory.Services.CreateScope();

    protected static async Task<byte[]> GetUniquePdfBytesAsync()
    {
        _cachedPdfBytes ??= await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf"));

        var randomSuffix = Guid.NewGuid().ToByteArray();
        var result = new byte[_cachedPdfBytes.Length + randomSuffix.Length];
        _cachedPdfBytes.CopyTo(result, 0);
        randomSuffix.CopyTo(result, _cachedPdfBytes.Length);
        return result;
    }

    protected async Task<Guid> UploadAndWaitAsync(HttpClient client, string name = "sample.pdf")
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", name);

        var response = await client.PostAsync("/api/v1/documents", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>();
        return await WaitForJobCompletionAsync(body!.JobId);
    }

    protected static async Task<HttpResponseMessage> SendJsonPatchAsync(
        HttpClient client, string url, object patchOperations)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(patchOperations)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json-patch+json");
        return await client.SendAsync(request);
    }

    protected async Task<Guid> WaitForJobCompletionAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (DateTime.UtcNow < deadline)
        {
            var job = await db.ProcessingJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jobId);

            if (job is not null)
            {
                if (job.Status == ProcessingJobStatus.Completed)
                    return job.EntityId ?? Guid.Empty;

                if (job.Status == ProcessingJobStatus.Failed)
                    throw new Exception($"Processing job {jobId} failed: {job.Error}");
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Processing job {jobId} did not complete within timeout");
    }
}
