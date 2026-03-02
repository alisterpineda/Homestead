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
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>();
        body!.Name.Should().Be("Acme Corp");
        body.Description.Should().Be("A test party");
        body.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetParties_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await CreatePartyAsync(client, "Party A");
        await CreatePartyAsync(client, "Party B");

        var response = await client.GetAsync("/api/v1/documents/parties?Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>();
        body!.Items.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetParty_ReturnsDetails()
    {
        var client = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(client, "Detail Party");

        var response = await client.GetAsync($"/api/v1/documents/parties/{party.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>();
        body!.Name.Should().Be("Detail Party");
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
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PartyResult>();
        body!.Name.Should().Be("Updated");
        body.Description.Should().Be("New description");
    }

    [Fact]
    public async Task DeleteParty_Removes()
    {
        var client = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(client, "ToDelete");

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/parties/{party.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/documents/parties/{party.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>();
        body!.DeletedCount.Should().Be(2);
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
