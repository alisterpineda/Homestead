using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class BulkReprocessDocumentsTests : IntegrationTestBase
{
    public BulkReprocessDocumentsTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BulkReprocess_ReturnsAccepted_WithJobIds()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "doc1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "doc2.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId1, docId2 } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BulkReprocessResult>(CT);
        Assert.Equal(2, body!.JobIds.Count);

        foreach (var jobId in body.JobIds)
        {
            await WaitForJobCompletionAsync(jobId);
        }
    }

    [Fact]
    public async Task BulkReprocess_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { Guid.NewGuid() } }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BulkReprocess_Viewer_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient);

        var viewerClient = await CreateViewerClientAsync();
        var response = await viewerClient.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId } }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BulkReprocess_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = Array.Empty<Guid>() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkReprocess_WithEngineId_ReturnsAccepted()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId }, textExtractionEngineId = builtInId }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BulkReprocessResult>(CT);
        Assert.Single(body!.JobIds);

        await WaitForJobCompletionAsync(body.JobIds[0]);
    }

    [Fact]
    public async Task BulkReprocess_NonExistentEngineId_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId }, textExtractionEngineId = Guid.NewGuid() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkReprocess_NonExistentDocumentIds_ReturnsAccepted_WithEmptyJobIds()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { Guid.NewGuid() } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BulkReprocessResult>(CT);
        Assert.Empty(body!.JobIds);
    }
}
