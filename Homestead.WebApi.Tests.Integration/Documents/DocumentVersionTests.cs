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

        var response = await client.PostAsync($"/api/v1/documents/{docId}/versions", form);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>();
        await WaitForJobCompletionAsync(jobBody!.JobId);

        var versionsResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10");
        versionsResponse.EnsureSuccessStatusCode();
        var versions = await versionsResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>();
        versions!.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetVersions_ReturnsPaginatedList()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>();
        body!.Items.Should().NotBeEmpty();
        body.Items.First().VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetVersion_ReturnsSpecificVersion()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10");
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>();
        var versionId = list!.Items.First().Id;

        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions/{versionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VersionDetail>();
        body!.Id.Should().Be(versionId);
        body.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task PatchVersion_UpdatesLabel()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10");
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>();
        var versionId = list!.Items.First().Id;

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}/versions/{versionId}", new[]
        {
            new { op = "replace", path = "/label", value = (object)"Updated Label" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VersionDetail>();
        body!.Label.Should().Be("Updated Label");
    }

    [Fact]
    public async Task DeleteVersion_OnlyVersion_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var listResponse = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10");
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>();
        var versionId = list!.Items.First().Id;

        var response = await client.DeleteAsync($"/api/v1/documents/{docId}/versions/{versionId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDocumentFile_DownloadsFile()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/file");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
    }

    private record VersionItem(Guid Id, int VersionNumber, string? Label, string ContentType, long FileSize, string FileExtension);
    private record VersionDetail(Guid Id, int VersionNumber, string? Label, string ContentType, long FileSize, string FileExtension, DateTimeOffset CreatedAt);
}
