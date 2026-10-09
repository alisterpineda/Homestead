using System.IO.Compression;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DownloadTests : IntegrationTestBase
{
    public DownloadTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DownloadDocuments_Returns202WithJobId()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/download", new { }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, body!.JobId);
    }

    [Fact]
    public async Task DownloadDocuments_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/download", new { }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DownloadDocuments_EmptyDb_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = (List<Guid>?)null }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DownloadDocuments_SelectedIds_CompletesAndProducesZip()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "DownloadMe.pdf");

        var downloadResponse = await client.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = new[] { docId } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, downloadResponse.StatusCode);
        var downloadBody = await downloadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);

        // Wait for the download job to complete
        await WaitForJobCompletionAsync(downloadBody!.JobId);

        // Download the zip
        var fileResponse = await client.GetAsync(
            $"/api/v1/documents/download/{downloadBody.JobId}", CT);

        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal("application/zip", fileResponse.Content.Headers.ContentType?.MediaType);

        // Verify zip has content
        var zipBytes = await fileResponse.Content.ReadAsByteArrayAsync(CT);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.True(archive.Entries.Count >= 1);

        // Verify the file is named correctly (untagged since no tags assigned)
        var entry = archive.Entries.First();
        Assert.Contains("DownloadMe", entry.FullName);
        Assert.EndsWith(".pdf", entry.FullName);
    }

    [Fact]
    public async Task DownloadDocuments_DownloadAll_IncludesAllDocuments()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");
        await UploadAndWaitAsync(client, "Doc2.pdf");

        var downloadResponse = await client.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = (List<Guid>?)null }, CT);

        Assert.Equal(HttpStatusCode.Accepted, downloadResponse.StatusCode);
        var downloadBody = await downloadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(downloadBody!.JobId);

        var fileResponse = await client.GetAsync(
            $"/api/v1/documents/download/{downloadBody.JobId}", CT);

        var zipBytes = await fileResponse.Content.ReadAsByteArrayAsync(CT);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.True(archive.Entries.Count >= 2);
    }

    [Fact]
    public async Task GetDownloadFile_InvalidJobId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/documents/download/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDownloadFile_OtherUsersDownload_Returns404()
    {
        var admin = await CreateAdminClientAsync();
        await UploadAndWaitAsync(admin, "AdminDoc.pdf");

        var downloadResponse = await admin.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = (List<Guid>?)null }, CT);
        var downloadBody = await downloadResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(downloadBody!.JobId);

        // Different user tries to download
        var otherUser = await CreateUserClientAsync();
        var fileResponse = await otherUser.GetAsync(
            $"/api/v1/documents/download/{downloadBody.JobId}", CT);

        Assert.Equal(HttpStatusCode.NotFound, fileResponse.StatusCode);
    }

    [Fact]
    public async Task DownloadDocuments_ConcurrentDownload_Returns400()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "ConcurrentDoc.pdf");

        // Hold the worker so the first download stays pending — otherwise it can finish before the second request.
        await using var _ = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);

        var first = await client.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = (List<Guid>?)null }, CT);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/documents/download",
            new { ids = (List<Guid>?)null }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

}
