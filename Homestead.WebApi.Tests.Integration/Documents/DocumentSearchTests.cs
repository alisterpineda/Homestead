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

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=UniqueInvoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "UniqueInvoice.pdf");
    }

    [Fact]
    public async Task GetDocuments_SearchNoMatch_ReturnsEmpty()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "RealDocument.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=xyznonexistent", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task GetDocuments_FilterByTag_ReturnsTagged()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var tagResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "FilterTag" }, CT);
        tagResponse.EnsureSuccessStatusCode();
        var tag = await tagResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "filtertag" } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&TagIds={tag!.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_FilterByParty_ReturnsLinked()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "FilterParty" }, CT);
        partyResponse.EnsureSuccessStatusCode();
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyResult>(CT);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { party!.Id } }
        });
        patchResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&PartyIds={party.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task GetDocuments_SortByName_OrdersCorrectly()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Bravo.pdf");
        await UploadAndWaitAsync(client, "Alpha.pdf");
        await UploadAndWaitAsync(client, "Charlie.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&SortBy=Name&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        var names = body!.Items.Select(d => d.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public async Task GetDocuments_Pagination_RespectsPageSize()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 0; i < 5; i++)
            await UploadAndWaitAsync(client, $"Page{i}.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=2&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Equal(2, body!.Items.Count);
        Assert.Equal(5, body.TotalCount);
    }

    [Fact]
    public async Task GetDocuments_PrefixSearch_MatchesStemmedWord()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Consultation Agreement.pdf");
        await UploadAndWaitAsync(client, "Unrelated Report.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=consult", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Consultation Agreement.pdf");
    }

    [Fact]
    public async Task GetDocuments_ShortPrefix_MatchesViaILike()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Consultation Agreement.pdf");
        await UploadAndWaitAsync(client, "Unrelated Report.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=consu", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Consultation Agreement.pdf");
    }

    [Fact]
    public async Task GetDocuments_MultiWordSearch_MatchesBothTerms()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Tax Invoice 2024.pdf");
        await UploadAndWaitAsync(client, "Tax Return 2024.pdf");
        await UploadAndWaitAsync(client, "Sales Invoice 2024.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=tax+invoice", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Single(body!.Items, d => d.Name == "Tax Invoice 2024.pdf");
    }

    [Fact]
    public async Task GetDocuments_SpecialCharactersInSearch_DoesNotThrow()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Normal Document.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false&Search=test%26%26%7C%7C!%27%3A*", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private record DocumentListItem(Guid Id, string Name, string ContentType, long FileSize, string FileExtension);
    private record TagResult(Guid Id, string Name, string Path);
    private record PartyResult(Guid Id, string Name);
}
