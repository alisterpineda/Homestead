using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class PartyTests : IntegrationTestBase
{
    public PartyTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateParty_ReturnsCreated()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new
        {
            name = "Acme Corp",
            description = "A test party"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>(CT);
        Assert.Equal("Acme Corp", body!.Name);
        Assert.Equal("A test party", body.Description);
        Assert.NotEqual(Guid.Empty, body.Id);
    }

    [Fact]
    public async Task GetParties_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Party A");
        await CreatePartyAsync(client, "Party B");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        Assert.True(body!.Items.Count >= 2);
    }

    [Fact]
    public async Task GetParty_ReturnsDetails()
    {
        var client = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(client, "Detail Party");

        var response = await client.GetAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>(CT);
        Assert.Equal("Detail Party", body!.Name);
    }

    [Fact]
    public async Task UpdateParty_ModifiesFields()
    {
        var client = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(client, "Original");

        var response = await client.PutAsJsonAsync($"/api/v1/documents/parties/{party.Id}", new
        {
            name = "Updated",
            description = "New description"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>(CT);
        Assert.Equal("Updated", body!.Name);
        Assert.Equal("New description", body.Description);
    }

    [Fact]
    public async Task DeleteParty_Removes()
    {
        var client = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(client, "ToDelete");

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/parties/{party.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/documents/parties/{party.Id}", CT);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task BulkDeleteParties_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();
        var party1 = await CreatePartyAsync(client, "Bulk1");
        var party2 = await CreatePartyAsync(client, "Bulk2");

        var response = await client.PostAsJsonAsync("/api/v1/documents/parties/bulk-delete", new
        {
            ids = new[] { party1.Id, party2.Id }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    private async Task<PartyResult> CreatePartyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new
        {
            name,
            description = $"Description for {name}"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PartyResult>())!;
    }

    [Fact]
    public async Task GetParties_SortByNameAsc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Charlie");
        await CreatePartyAsync(client, "Alpha");
        await CreatePartyAsync(client, "Bravo");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10&SortBy=Name&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var names = body!.Items.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetParties_SortByNameDesc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Charlie");
        await CreatePartyAsync(client, "Alpha");
        await CreatePartyAsync(client, "Bravo");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10&SortBy=Name&SortDir=Desc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var names = body!.Items.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderByDescending(n => n), names);
    }

    [Fact]
    public async Task GetParties_SortByCreatedAtDesc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "First");
        await Task.Delay(50);
        await CreatePartyAsync(client, "Second");
        await Task.Delay(50);
        await CreatePartyAsync(client, "Third");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10&SortBy=CreatedAt&SortDir=Desc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var names = body!.Items.Select(p => p.Name).ToList();
        Assert.Equal("Third", names[0]);
        Assert.Equal("Second", names[1]);
        Assert.Equal("First", names[2]);
    }

    [Fact]
    public async Task GetParties_DefaultSort_IsByNameAsc()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Zulu");
        await CreatePartyAsync(client, "Alpha");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var names = body!.Items.Select(p => p.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetParties_SortByDocumentCountDesc_ReturnsMostDocsFirst()
    {
        var client = await CreateAdminClientAsync();
        var partyMany = await CreatePartyAsync(client, "Many Docs Party");
        var partyOne = await CreatePartyAsync(client, "One Doc Party");
        await CreatePartyAsync(client, "No Docs Party");

        // Assign 2 documents to partyMany
        var doc1 = await UploadAndWaitAsync(client, "sort-count-1.pdf");
        var doc2 = await UploadAndWaitAsync(client, "sort-count-2.pdf");
        await AssignPartyAsync(client, doc1, partyMany.Id);
        await AssignPartyAsync(client, doc2, partyMany.Id);

        // Assign 1 document to partyOne
        await AssignPartyAsync(client, doc1, partyOne.Id);

        var response = await client.GetAsync(
            "/api/v1/documents/parties?Page=1&PageSize=10&SortBy=DocumentCount&SortDir=Desc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var items = body!.Items;
        Assert.Equal("Many Docs Party", items[0].Name);
        Assert.Equal(2, items[0].DocumentCount);
        Assert.Equal("No Docs Party", items[^1].Name);
        Assert.Equal(0, items[^1].DocumentCount);
    }

    [Fact]
    public async Task GetParties_SearchWithSort_FiltersAndSorts()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Zeta Corp");
        await CreatePartyAsync(client, "Alpha Corp");
        await CreatePartyAsync(client, "Beta Industries");

        var response = await client.GetAsync(
            "/api/v1/documents/parties?Page=1&PageSize=10&Search=corp&SortBy=Name&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var names = body!.Items.Select(p => p.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.Equal("Alpha Corp", names[0]);
        Assert.Equal("Zeta Corp", names[1]);
    }

    private async Task AssignPartyAsync(HttpClient client, Guid docId, Guid partyId)
    {
        // Get current parties
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        getResponse.EnsureSuccessStatusCode();
        var doc = await getResponse.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        var currentPartyIds = doc!.Parties.Select(p => p.Id).ToList();
        currentPartyIds.Add(partyId);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)currentPartyIds.Distinct().ToArray() }
        });
        patchResponse.EnsureSuccessStatusCode();
    }

    private record PartyResult(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt);
    private record PartyListItem(Guid Id, string Name, string? Description, int DocumentCount);
    private record DocumentDetail(Guid Id, string Name, IReadOnlyList<DocumentDetailParty> Parties);
    private record DocumentDetailParty(Guid Id, string Name);
}
