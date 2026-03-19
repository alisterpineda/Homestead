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
    public async Task ReprocessVersion_WithEngineIdBody_ReturnsAccepted()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        listResponse.EnsureSuccessStatusCode();
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        // Use built-in engine ID in the body
        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { textExtractionEngineId = builtInId }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, jobBody!.JobId);

        await WaitForJobCompletionAsync(jobBody.JobId);
    }

    [Fact]
    public async Task ReprocessVersion_WithNullBody_ReturnsAccepted()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        listResponse.EnsureSuccessStatusCode();
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        // Send with null engine ID in the body (uses default)
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { textExtractionEngineId = (Guid?)null }, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, jobBody!.JobId);

        await WaitForJobCompletionAsync(jobBody.JobId);
    }

    [Fact]
    public async Task ReprocessVersion_WithNonExistentEngineId_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        listResponse.EnsureSuccessStatusCode();
        var versions = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = versions!.Items.First().Id;

        var fakeEngineId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/versions/{versionId}/reprocess",
            new { textExtractionEngineId = fakeEngineId }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
