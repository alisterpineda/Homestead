using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DuplicateDocumentTests : IntegrationTestBase
{
    public DuplicateDocumentTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DuplicateUpload_FailsJob_WithDuplicateError()
    {
        var client = await CreateAdminClientAsync();

        // First upload — should succeed
        var response1 = await UploadFixedPdfAsync(client, "Original.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        var body1 = await response1.Content.ReadFromJsonAsync<JobResponse>(CT);
        var firstDocId = await WaitForJobCompletionAsync(body1!.JobId);

        // Second upload with same content — should fail as duplicate
        var response2 = await UploadFixedPdfAsync(client, "Duplicate.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);
        var body2 = await response2.Content.ReadFromJsonAsync<JobResponse>(CT);
        var failedJob = await WaitForJobFailureAsync(body2!.JobId);

        Assert.Contains("already exists", failedJob.Error);
    }

    [Fact]
    public async Task DuplicateUpload_TrashedOriginal_StillDetectedAsDuplicate()
    {
        var client = await CreateAdminClientAsync();

        // Upload the file
        var response1 = await UploadFixedPdfAsync(client, "WillBeDeleted.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        var body1 = await response1.Content.ReadFromJsonAsync<JobResponse>(CT);
        var docId = await WaitForJobCompletionAsync(body1!.JobId);

        // Soft-delete (trash) the document
        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Re-upload same content — still detected as duplicate because the unique index
        // IX_document_versions_FileHash is unconditional (not filtered by document deletion)
        var response2 = await UploadFixedPdfAsync(client, "Reupload.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);
        var body2 = await response2.Content.ReadFromJsonAsync<JobResponse>(CT);
        var failedJob = await WaitForJobFailureAsync(body2!.JobId);

        Assert.Contains("already exists", failedJob.Error);
    }

    [Fact]
    public async Task DuplicateUpload_ExistingDocumentStillAccessible()
    {
        var client = await CreateAdminClientAsync();

        // First upload — should succeed
        var response1 = await UploadFixedPdfAsync(client, "Original.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode);
        var body1 = await response1.Content.ReadFromJsonAsync<JobResponse>(CT);
        var firstDocId = await WaitForJobCompletionAsync(body1!.JobId);

        // Second upload with same content — should fail
        var response2 = await UploadFixedPdfAsync(client, "Duplicate.pdf");
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode);
        var body2 = await response2.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobFailureAsync(body2!.JobId);

        // Original document should still be accessible
        var getResponse = await client.GetAsync($"/api/v1/documents/{firstDocId}", CT);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }
}
