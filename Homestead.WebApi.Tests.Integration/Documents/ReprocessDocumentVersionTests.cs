using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class ReprocessDocumentVersionTests : IntegrationTestBase
{
    public ReprocessDocumentVersionTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ReprocessVersion_ReturnsAccepted_AndCompletesJob()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        // Get version ID
        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        listResponse.EnsureSuccessStatusCode();
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        // Call reprocess endpoint
        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess", null, CT);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, jobBody!.JobId);

        await WaitForJobCompletionAsync(jobBody.JobId);
    }

    [Fact]
    public async Task ReprocessVersion_Unauthenticated_Returns401()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient);

        var listResponse = await adminClient.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        var client = CreateAnonymousClient();
        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess", null, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReprocessVersion_Viewer_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient);

        var listResponse = await adminClient.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        var viewerClient = await CreateViewerClientAsync();
        var response = await viewerClient.PostAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess", null, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReprocessVersion_InvalidDocumentId_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var fakeDocId = Guid.NewGuid();
        var fakeVersionId = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/api/v1/documents/{fakeDocId}/versions/{fakeVersionId}/reprocess", null, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReprocessVersion_InvalidVersionId_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);
        var fakeVersionId = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/api/v1/documents/{docId}/versions/{fakeVersionId}/reprocess", null, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReprocessVersion_WithEmptyJsonBody_ReturnsAccepted()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        listResponse.EnsureSuccessStatusCode();
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        // The endpoint still accepts an (ignored) JSON body — guards against a 415/400 regression now that
        // ReprocessDocumentVersionBody is an empty record.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, jobBody!.JobId);

        await WaitForJobCompletionAsync(jobBody.JobId);
    }
}
