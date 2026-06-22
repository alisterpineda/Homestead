using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Single(body!.JobIds);

        await WaitForJobCompletionAsync(body.JobIds[0]);
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
    public async Task BulkReprocess_NonExistentDocumentIds_ReturnsAccepted_WithEmptyJobIds()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { Guid.NewGuid() } }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BulkReprocessResult>(CT);
        Assert.Empty(body!.JobIds);
    }

    [Fact]
    public async Task BulkReprocess_SelectsLatestVersionPerDocument()
    {
        // The parent job handler picks the latest version per document via
        // DocumentVersions.GroupBy(DocumentId).Select(g => g.OrderByDescending(VersionNumber).Select(Id).First()).
        // EF InMemory evaluates that correlated First() client-side regardless of translation, so the unit
        // layer can't prove Npgsql translates it and selects the MAX-VersionNumber row — this real-Postgres
        // test does.
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "doc-latest.pdf"); // VersionNumber 1

        // Add a second version (VersionNumber 2) so "latest" is unambiguous and differs from the first.
        var v2Bytes = await GetUniquePdfBytesAsync();
        var versionForm = new MultipartFormDataContent
        {
            { new StringContent("V2"), "label" },
            { new ByteArrayContent(v2Bytes), "file", "doc-latest-v2.pdf" },
        };
        var versionResp = await client.PostAsync($"/api/v1/documents/{docId}/versions", versionForm, CT);
        Assert.Equal(HttpStatusCode.Accepted, versionResp.StatusCode);
        var versionJob = await versionResp.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(versionJob!.JobId);

        // Compute the expected latest version id independently of the SUT query.
        Guid expectedLatestVersionId;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            expectedLatestVersionId = await db.DocumentVersions
                .Where(v => v.DocumentId == docId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.Id)
                .FirstAsync(CT);
        }

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-reprocess",
            new { ids = new[] { docId } }, CT);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkReprocessResult>(CT);
        var parentJobId = body!.JobIds[0];

        // Waiting on the PARENT job guarantees its handler ran and inserted the child rows (the child Payload
        // is written at insert time, so we need not wait for the child job itself to complete).
        await WaitForJobCompletionAsync(parentJobId);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var childJob = await db.ProcessingJobs.AsNoTracking()
                .Where(j => j.ParentJobId == parentJobId && j.EntityId == docId)
                .SingleAsync(CT);
            Assert.Equal(ProcessingJobType.ReprocessDocumentVersion, childJob.Type);

            var childPayload = childJob.GetPayload<ReprocessDocumentVersionPayload>();
            Assert.NotNull(childPayload);
            Assert.Equal(docId, childPayload!.DocumentId);
            Assert.Equal(expectedLatestVersionId, childPayload.VersionId);
        }
    }
}
