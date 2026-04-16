using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentVersionTests : IntegrationTestBase
{
    public DocumentVersionTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateVersion_AddsNewVersion()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("V2"), "label");
        form.Add(new ByteArrayContent(fileBytes), "file", "sample-v2.pdf");

        var response = await client.PostAsync($"/api/v1/documents/{docId}/versions", form, CT);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(jobBody!.JobId);

        var versionsResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        versionsResponse.EnsureSuccessStatusCode();
        var versions = await versionsResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        Assert.True(versions!.TotalCount >= 2);
    }

    [Fact]
    public async Task GetVersions_ReturnsPaginatedList()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        Assert.NotEmpty(body!.Items);
        Assert.Equal(1, body.Items.First().VersionNumber);
    }

    [Fact]
    public async Task GetVersion_ReturnsSpecificVersion()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = list!.Items.First().Id;

        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VersionDetail>(CT);
        Assert.Equal(versionId, body!.Id);
        Assert.Equal(1, body.VersionNumber);
    }

    [Fact]
    public async Task PatchVersion_UpdatesLabel()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = list!.Items.First().Id;

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}/versions/{versionId}", new[]
        {
            new { op = "replace", path = "/label", value = (object)"Updated Label" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VersionDetail>(CT);
        Assert.Equal("Updated Label", body!.Label);
    }

    [Fact]
    public async Task DeleteVersion_OnlyVersion_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = list!.Items.First().Id;

        var response = await client.DeleteAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentFile_DownloadsFile()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/file", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(CT);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task GetVersions_Unauthenticated_Returns401()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient);

        var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetVersion_Unauthenticated_Returns401()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient);

        var listResponse = await adminClient.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = list!.Items.First().Id;

        var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchVersion_SetsUpdatedAt()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        var versionId = list!.Items.First().Id;

        // Get version before patch to capture initial UpdatedAt
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);
        var before = await getResponse.Content.ReadFromJsonAsync<VersionDetail>(CT);
        var initialUpdatedAt = before!.UpdatedAt;

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}/versions/{versionId}", new[]
        {
            new { op = "replace", path = "/label", value = (object)"Patched Label" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VersionDetail>(CT);
        Assert.True(body!.UpdatedAt > initialUpdatedAt);
    }

    private record VersionDetail(Guid Id, int VersionNumber, string? Label, string ContentType, long FileSize, string FileExtension,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt = default);
}
