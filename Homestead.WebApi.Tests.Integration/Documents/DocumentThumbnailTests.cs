using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentThumbnailTests : IntegrationTestBase
{
    public DocumentThumbnailTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetThumbnail_AfterUploadPdf_Returns200WithWebp()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "test.pdf");

        var response = await client.GetAsync($"/api/v1/documents/{docId}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetThumbnail_AfterMultiPagePdfUpload_RendersNonTrivialImage()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadFixtureAndWaitAsync(client, "nist-sp-250-93-vacuum-calibration.pdf");

        var response = await client.GetAsync($"/api/v1/documents/{docId}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsByteArrayAsync(CT);
        Assert.True(body.Length > 1024, $"Expected thumbnail > 1024 bytes, got {body.Length}");
    }

    [Fact]
    public async Task GetThumbnail_AfterUploadImage_Returns200WithWebp()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadImageAndWaitAsync(client, "photo.png");

        var response = await client.GetAsync($"/api/v1/documents/{docId}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetThumbnail_UnsupportedContentType_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadTextAndWaitAsync(client, "notes.txt");

        var response = await client.GetAsync($"/api/v1/documents/{docId}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetThumbnail_InvalidDocId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetThumbnail_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_HasThumbnail_TrueForPdf()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "thumb-test.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentWithThumbnail>>(CT);
        var doc = body!.Items.FirstOrDefault(d => d.Name == "thumb-test.pdf");
        Assert.NotNull(doc);
        Assert.True(doc.HasThumbnail);
    }

    [Fact]
    public async Task GetDocuments_HasThumbnail_FalseForTextFile()
    {
        var client = await CreateAdminClientAsync();
        await UploadTextAndWaitAsync(client, "plain.txt");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentWithThumbnail>>(CT);
        var doc = body!.Items.FirstOrDefault(d => d.Name == "plain.txt");
        Assert.NotNull(doc);
        Assert.False(doc.HasThumbnail);
    }

    [Fact]
    public async Task GetThumbnail_OnDemandGeneration_Works()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadImageAndWaitAsync(client, "ondemand.png");

        // Delete the cached thumbnail
        var thumbnailsDir = Path.Combine(Factory.CachePath, "thumbnails");
        if (Directory.Exists(thumbnailsDir))
            Directory.Delete(thumbnailsDir, recursive: true);

        // Request again — should regenerate on-demand
        var response = await client.GetAsync($"/api/v1/documents/{docId}/thumbnail", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
    }

    private record DocumentWithThumbnail(Guid Id, string Name, bool HasThumbnail);
}
