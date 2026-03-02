using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentCrudTests : IntegrationTestBase
{
    public DocumentCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateDocument_UploadsFile_Returns202WithJobId()
    {
        var client = await CreateAdminClientAsync();

        var response = await UploadDocumentAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        body!.JobId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateDocument_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await UploadDocumentAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDocuments_ReturnsPagedList()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");
        await UploadAndWaitAsync(client, "Doc2.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        body!.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        body.Items.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetDocuments_EmptyDb_ReturnsEmptyList()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        body!.TotalCount.Should().Be(0);
        body.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocument_ValidId_ReturnsDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "TestDoc.pdf");

        var response = await client.GetAsync($"/api/v1/documents/{docId}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        body!.Id.Should().Be(docId);
        body.Name.Should().Be("TestDoc.pdf");
    }

    [Fact]
    public async Task GetDocument_InvalidId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}", CT);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchDocument_UpdatesName()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"Updated Name" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        body!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task DeleteDocument_RemovesDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BulkDeleteDocuments_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "Bulk1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "Bulk2.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-delete", new
        {
            ids = new[] { docId1, docId2 }
        }, CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        body!.DeletedCount.Should().Be(2);
    }

    private async Task<HttpResponseMessage> UploadDocumentAsync(HttpClient client, string name = "sample.pdf")
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", name);

        return await client.PostAsync("/api/v1/documents", form);
    }

    private record DocumentListItem(Guid Id, string Name, string ContentType, long FileSize, string FileExtension);
    private record DocumentDetail(Guid Id, string Name, string ContentType, long FileSize, string FileExtension, DateTimeOffset CreatedAt);
}
