using System.IO.Compression;
using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class ExportTests : IntegrationTestBase
{
    public ExportTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ExportDocuments_Returns202WithJobId()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/export", new { }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, body!.JobId);
    }

    [Fact]
    public async Task ExportDocuments_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/export", new { }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExportDocuments_EmptyDb_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportDocuments_SelectedIds_CompletesAndProducesZip()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "ExportMe.pdf");

        var exportResponse = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = new[] { docId } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
        var exportBody = await exportResponse.Content.ReadFromJsonAsync<JobResponse>(CT);

        // Wait for the export job to complete
        await WaitForJobCompletionAsync(exportBody!.JobId);

        // Download the zip
        var downloadResponse = await client.GetAsync(
            $"/api/v1/documents/export/{exportBody.JobId}/download", CT);

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("application/zip", downloadResponse.Content.Headers.ContentType?.MediaType);

        // Verify zip has content
        var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync(CT);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.True(archive.Entries.Count >= 1);

        // Verify the file is named correctly (untagged since no tags assigned)
        var entry = archive.Entries.First();
        Assert.Contains("ExportMe", entry.FullName);
        Assert.EndsWith(".pdf", entry.FullName);
    }

    [Fact]
    public async Task ExportDocuments_ExportAll_IncludesAllDocuments()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");
        await UploadAndWaitAsync(client, "Doc2.pdf");

        var exportResponse = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);

        Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
        var exportBody = await exportResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(exportBody!.JobId);

        var downloadResponse = await client.GetAsync(
            $"/api/v1/documents/export/{exportBody.JobId}/download", CT);

        var zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync(CT);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.True(archive.Entries.Count >= 2);
    }

    [Fact]
    public async Task GetExports_ReturnsUserExports()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "ForExport.pdf");

        var exportResponse = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);
        var exportBody = await exportResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(exportBody!.JobId);

        var listResponse = await client.GetAsync("/api/v1/documents/export?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var body = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<ExportItem>>(CT);
        Assert.True(body!.TotalCount >= 1);
        var item = body.Items.First(i => i.Id == exportBody.JobId);
        Assert.Equal("Completed", item.Status);
        Assert.True(item.DocumentCount > 0);
        Assert.True(item.ZipFileSize > 0);
    }

    [Fact]
    public async Task DownloadExport_InvalidJobId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/documents/export/{Guid.NewGuid()}/download", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadExport_OtherUsersExport_Returns404()
    {
        var admin = await CreateAdminClientAsync();
        await UploadAndWaitAsync(admin, "AdminDoc.pdf");

        var exportResponse = await admin.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);
        var exportBody = await exportResponse.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(exportBody!.JobId);

        // Different user tries to download
        var otherUser = await CreateUserClientAsync();
        var downloadResponse = await otherUser.GetAsync(
            $"/api/v1/documents/export/{exportBody.JobId}/download", CT);

        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task ExportDocuments_ConcurrentExport_Returns400()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "ConcurrentDoc.pdf");

        // Start first export
        var first = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        // Immediately try a second export (first may still be processing)
        var second = await client.PostAsJsonAsync("/api/v1/documents/export",
            new { ids = (List<Guid>?)null }, CT);

        // Should be rejected (400) if first is still pending/in-progress
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    private record ExportItem(Guid Id, string Status, int? DocumentCount, long? ZipFileSize,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ExpiresAt);
}
