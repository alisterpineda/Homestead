using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
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

    protected static CancellationToken CT => TestContext.Current.CancellationToken;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var control = Factory.Services.GetRequiredService<IJobProcessorControl>();
        await using (await control.PauseAsync())
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await _fixture.Respawner.ResetAsync(conn);
                    break;
                }
                catch (PostgresException ex) when (ex.SqlState == "40P01" && attempt < 2)
                {
                    await Task.Delay(100);
                }
            }
        }

        // Respawn clears all finances tables (including the ISO 4217 commodity
        // seed) between tests. UseSeeding only fires on Migrate/EnsureCreated,
        // not on Respawn's raw-SQL wipes, so restore the seed explicitly.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            CurrencySeeder.Seed(db);
        }

        // Clean stored files to avoid duplicate-content errors across test classes
        var documentsDir = Path.Combine(Factory.MediaPath, "documents");
        try { Directory.Delete(documentsDir, recursive: true); } catch (DirectoryNotFoundException) { }
        Directory.CreateDirectory(documentsDir);

        // Clean thumbnail cache
        var thumbnailsDir = Path.Combine(Factory.CachePath, "thumbnails");
        try { Directory.Delete(thumbnailsDir, recursive: true); } catch (DirectoryNotFoundException) { }
        Directory.CreateDirectory(thumbnailsDir);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected HttpClient CreateAnonymousClient()
    {
        return Factory.CreateClient();
    }

    protected async Task<HttpClient> CreateAdminClientAsync(bool isActive = true)
    {
        return await AuthHelper.CreateAdminClientAsync(Factory, isActive: isActive);
    }

    protected async Task<HttpClient> CreateUserClientAsync(bool isActive = true, string role = "Contributor")
    {
        return await AuthHelper.CreateUserClientAsync(Factory, isActive: isActive, role: role);
    }

    protected Task<HttpClient> CreateViewerClientAsync(bool isActive = true)
        => CreateUserClientAsync(isActive: isActive, role: "Viewer");

    protected Task<HttpClient> CreateGuestClientAsync(bool isActive = true)
        => CreateUserClientAsync(isActive: isActive, role: "Guest");

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

    protected async Task<Guid> UploadImageAndWaitAsync(HttpClient client, string name = "sample.png")
    {
        var imageBytes = await GetUniqueImageBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", name);

        var response = await client.PostAsync("/api/v1/documents", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>();
        return await WaitForJobCompletionAsync(body!.JobId);
    }

    protected async Task<Guid> UploadTextAndWaitAsync(HttpClient client, string name = "sample.txt")
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes($"Test content {Guid.NewGuid()}");
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        var fileContent = new ByteArrayContent(textBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", name);

        var response = await client.PostAsync("/api/v1/documents", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JobResponse>();
        return await WaitForJobCompletionAsync(body!.JobId);
    }

    protected static Task<byte[]> GetUniqueImageBytesAsync()
    {
        // Create a small unique PNG using SkiaSharp
        using var bitmap = new SkiaSharp.SKBitmap(100, 100);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor((byte)(Random.Shared.Next(256)), (byte)(Random.Shared.Next(256)), (byte)(Random.Shared.Next(256))));

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Task.FromResult(data.ToArray());
    }

    protected async Task<Guid> UploadAndWaitAsync(HttpClient client, string name = "sample.pdf", Guid? groupId = null)
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", name);
        if (groupId.HasValue)
            form.Add(new StringContent(groupId.Value.ToString()), "groupId");

        var response = await client.PostAsync("/api/v1/documents", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

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

    protected async Task<Guid> CreateGroupAsync(HttpClient adminClient, string name, string? description = null)
    {
        var response = await adminClient.PostAsJsonAsync("/api/v1/admin/groups", new
        {
            name,
            description
        }, CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GroupItem>(CT);
        return body!.Id;
    }

    protected async Task AddGroupMemberAsync(HttpClient adminClient, Guid groupId, Guid userId, string accessLevel = "Editor")
    {
        var response = await adminClient.PostAsJsonAsync($"/api/v1/admin/groups/{groupId}/members", new
        {
            userId,
            accessLevel
        }, CT);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<Guid> GetUserIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/user-info", CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UserInfoResult>(CT);
        return body!.Id;
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

    protected async Task<ProcessingJob> WaitForJobFailureAsync(Guid jobId, TimeSpan? timeout = null)
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
                if (job.Status == ProcessingJobStatus.Failed)
                    return job;

                if (job.Status == ProcessingJobStatus.Completed)
                    throw new Exception($"Processing job {jobId} completed unexpectedly (expected failure)");
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Processing job {jobId} did not fail within timeout");
    }

    protected static async Task<byte[]> GetFixedPdfBytes()
    {
        _cachedPdfBytes ??= await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pdf"));

        return _cachedPdfBytes;
    }

    protected async Task<HttpResponseMessage> UploadFixedPdfAsync(HttpClient client, string name = "sample.pdf")
    {
        var fileBytes = await GetFixedPdfBytes();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", name);

        return await client.PostAsync("/api/v1/documents", form);
    }
}
