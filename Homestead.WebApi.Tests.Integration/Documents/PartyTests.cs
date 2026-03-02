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

    private record PartyResult(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt);
    private record PartyListItem(Guid Id, string Name, string? Description, int DocumentCount);
}
