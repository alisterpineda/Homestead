using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentSearchTests : IntegrationTestBase
{
    public DocumentSearchTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetDocuments_SearchByName_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "UniqueInvoice.pdf");
        await UploadAndWaitAsync(client, "OtherContract.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=UniqueInvoice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        body!.Items.Should().ContainSingle(d => d.Name == "UniqueInvoice.pdf");
    }

    [Fact]
    public async Task GetDocuments_SearchNoMatch_ReturnsEmpty()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "RealDocument.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=xyznonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        body!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocuments_FilterByTag_ReturnsTagged()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var tagResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "FilterTag" });
        tagResponse.EnsureSuccessStatusCode();
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagResult>();

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "filtertag" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&TagIds={tag!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        body!.Items.Should().ContainSingle(d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_FilterByParty_ReturnsLinked()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "FilterParty" });
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>();

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&PartyIds={party.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        body!.Items.Should().ContainSingle(d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SortByName_OrdersCorrectly()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Bravo.pdf");
        await UploadAndWaitAsync(client, "Alpha.pdf");
        await UploadAndWaitAsync(client, "Charlie.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&SortBy=Name&SortDir=Asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        var names = body!.Items.Select(d => d.Name).ToList();
        names.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetDocuments_Pagination_RespectsPageSize()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 0; i < 5; i++)
            await UploadAndWaitAsync(client, $"Page{i}.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=2&IncludeChildTags=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>();
        body!.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(5);
    }

    private record DocumentListItem(Guid Id, string Name, string ContentType, long FileSize, string FileExtension);
    private record TagResult(Guid Id, string Name, string Path);
    private record PartyResult(Guid Id, string Name);
}
