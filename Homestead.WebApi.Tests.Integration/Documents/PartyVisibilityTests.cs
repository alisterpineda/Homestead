using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class PartyVisibilityTests : IntegrationTestBase
{
    public PartyVisibilityTests(PostgresFixture fixture) : base(fixture) { }

    // --- Visibility tests ---

    [Fact]
    public async Task Admin_SeesAllParties()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupA = await CreateGroupAsync(adminClient, "PartyVis Admin GroupA");
        var groupB = await CreateGroupAsync(adminClient, "PartyVis Admin GroupB");

        var partyA = await CreatePartyAsync(adminClient, "Admin Party A");
        var partyB = await CreatePartyAsync(adminClient, "Admin Party B");

        var docA = await UploadAndWaitAsync(adminClient, "admin-party-a.pdf", groupA);
        var docB = await UploadAndWaitAsync(adminClient, "admin-party-b.pdf", groupB);

        await AssignPartiesAsync(adminClient, docA, [partyA.Id]);
        await AssignPartiesAsync(adminClient, docB, [partyB.Id]);

        var response = await adminClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);

        Assert.Contains(body!.Items, p => p.Name == "Admin Party A");
        Assert.Contains(body.Items, p => p.Name == "Admin Party B");
    }

    [Fact]
    public async Task Contributor_SeesPartiesOnGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(adminClient, "Global Doc Party");
        var docId = await UploadAndWaitAsync(adminClient, "contrib-global-party.pdf");
        await AssignPartiesAsync(adminClient, docId, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);

        Assert.Contains(body!.Items, p => p.Name == "Global Doc Party");
    }

    [Fact]
    public async Task Contributor_SeesPartiesOnOtherGroupDocs_WithZeroDocCount()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyVis Other Group");

        var party = await CreatePartyAsync(adminClient, "Secret Group Party");
        var docB = await UploadAndWaitAsync(adminClient, "other-group-party.pdf", groupB);
        await AssignPartiesAsync(adminClient, docB, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);

        var matchedParty = body!.Items.FirstOrDefault(p => p.Name == "Secret Group Party");
        Assert.NotNull(matchedParty);
        Assert.Equal(0, matchedParty.DocumentCount);
    }

    [Fact]
    public async Task Guest_SeesAllParties_WithScopedDocCount()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupA = await CreateGroupAsync(adminClient, "PartyVis Guest GroupA");
        var groupB = await CreateGroupAsync(adminClient, "PartyVis Guest GroupB");

        var partyA = await CreatePartyAsync(adminClient, "Guest Visible Party");
        var partyB = await CreatePartyAsync(adminClient, "Guest Other Party");

        var docA = await UploadAndWaitAsync(adminClient, "guest-party-a.pdf", groupA);
        var docB = await UploadAndWaitAsync(adminClient, "guest-party-b.pdf", groupB);
        await AssignPartiesAsync(adminClient, docA, [partyA.Id]);
        await AssignPartiesAsync(adminClient, docB, [partyB.Id]);

        var guestClient = await CreateGuestClientAsync();
        var guestId = await GetUserIdAsync(guestClient);
        await AddGroupMemberAsync(adminClient, groupA, guestId, "Viewer");

        var response = await guestClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);

        var visibleParty = body!.Items.FirstOrDefault(p => p.Name == "Guest Visible Party");
        Assert.NotNull(visibleParty);
        Assert.True(visibleParty.DocumentCount > 0);

        var otherParty = body.Items.FirstOrDefault(p => p.Name == "Guest Other Party");
        Assert.NotNull(otherParty);
        Assert.Equal(0, otherParty.DocumentCount);
    }

    [Fact]
    public async Task OrphanedParty_VisibleToNonAdmin_WithZeroDocCount()
    {
        var adminClient = await CreateAdminClientAsync();
        await CreatePartyAsync(adminClient, "Orphaned Standalone Party");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);

        var matchedParty = body!.Items.FirstOrDefault(p => p.Name == "Orphaned Standalone Party");
        Assert.NotNull(matchedParty);
        Assert.Equal(0, matchedParty.DocumentCount);
    }

    [Fact]
    public async Task DocumentCount_ReflectsOnlyAccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyVis DocCount Group");

        var party = await CreatePartyAsync(adminClient, "DocCount Party");

        var globalDoc = await UploadAndWaitAsync(adminClient, "doccount-global.pdf");
        var groupDoc = await UploadAndWaitAsync(adminClient, "doccount-group.pdf", groupB);

        await AssignPartiesAsync(adminClient, globalDoc, [party.Id]);
        await AssignPartiesAsync(adminClient, groupDoc, [party.Id]);

        // Admin sees count = 2
        var adminResponse = await adminClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var adminBody = await adminResponse.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var adminParty = adminBody!.Items.First(p => p.Name == "DocCount Party");
        Assert.Equal(2, adminParty.DocumentCount);

        // Contributor (not a member of groupB) sees count = 1
        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contribResponse = await contributorClient.GetAsync("/api/v1/documents/parties?Page=1&PageSize=100", CT);
        var contribBody = await contribResponse.Content.ReadFromJsonAsync<PaginatedResult<PartyListItem>>(CT);
        var contribParty = contribBody!.Items.First(p => p.Name == "DocCount Party");
        Assert.Equal(1, contribParty.DocumentCount);
    }

    // --- GetParty visibility ---

    [Fact]
    public async Task GetParty_ReturnsOk_ForAnyParty()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyVis GetParty Group");

        var party = await CreatePartyAsync(adminClient, "Any Party");
        var docB = await UploadAndWaitAsync(adminClient, "any-party-doc.pdf", groupB);
        await AssignPartiesAsync(adminClient, docB, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Mutation authorization tests ---

    [Fact]
    public async Task Contributor_CanUpdateParty_OnlyOnAccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(adminClient, "Updatable Party");
        var docId = await UploadAndWaitAsync(adminClient, "update-party-ok.pdf");
        await AssignPartiesAsync(adminClient, docId, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.PutAsJsonAsync($"/api/v1/documents/parties/{party.Id}", new
        {
            name = "Updated Party",
            description = "New description"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotUpdateParty_UsedOnInaccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyMut Update Block Group");

        var party = await CreatePartyAsync(adminClient, "Shared Update Party");
        var globalDoc = await UploadAndWaitAsync(adminClient, "update-party-global.pdf");
        var groupDoc = await UploadAndWaitAsync(adminClient, "update-party-group.pdf", groupB);
        await AssignPartiesAsync(adminClient, globalDoc, [party.Id]);
        await AssignPartiesAsync(adminClient, groupDoc, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.PutAsJsonAsync($"/api/v1/documents/parties/{party.Id}", new
        {
            name = "Hacked Name",
            description = "Hacked"
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CanDeleteParty_OnlyOnAccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var party = await CreatePartyAsync(adminClient, "Deletable Party");
        var docId = await UploadAndWaitAsync(adminClient, "delete-party-ok.pdf");
        await AssignPartiesAsync(adminClient, docId, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotDeleteParty_UsedOnInaccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyMut Delete Block Group");

        var party = await CreatePartyAsync(adminClient, "Shared Delete Party");
        var globalDoc = await UploadAndWaitAsync(adminClient, "delete-party-global.pdf");
        var groupDoc = await UploadAndWaitAsync(adminClient, "delete-party-group.pdf", groupB);
        await AssignPartiesAsync(adminClient, globalDoc, [party.Id]);
        await AssignPartiesAsync(adminClient, groupDoc, [party.Id]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAlwaysModifyParties()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "PartyMut Admin Bypass Group");

        var party = await CreatePartyAsync(adminClient, "Admin Bypass Party");
        var groupDoc = await UploadAndWaitAsync(adminClient, "admin-modify-party.pdf", groupB);
        await AssignPartiesAsync(adminClient, groupDoc, [party.Id]);

        var response = await adminClient.PutAsJsonAsync($"/api/v1/documents/parties/{party.Id}", new
        {
            name = "Admin Renamed Party",
            description = "Admin updated"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotDeleteParty_OnGroupDocWithViewerAccess()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "PartyDel Viewer Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var party = await CreatePartyAsync(adminClient, "Viewer Delete Party");
        var docId = await UploadAndWaitAsync(adminClient, "party-del-viewer.pdf", groupId);
        await AssignPartiesAsync(adminClient, docId, [party.Id]);

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CanDeleteParty_OnGroupDocWithEditorAccess()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "PartyDel Editor Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        var party = await CreatePartyAsync(adminClient, "Editor Delete Party");
        var docId = await UploadAndWaitAsync(adminClient, "party-del-editor.pdf", groupId);
        await AssignPartiesAsync(adminClient, docId, [party.Id]);

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/parties/{party.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Helpers ---

    private async Task<PartyResult> CreatePartyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new
        {
            name,
            description = $"Description for {name}"
        }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PartyResult>(CT))!;
    }

    private async Task AssignPartiesAsync(HttpClient client, Guid docId, Guid[] partyIds)
    {
        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)partyIds }
        });
        response.EnsureSuccessStatusCode();
    }

    private record PartyResult(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt);
    private record PartyListItem(Guid Id, string Name, string? Description, int DocumentCount);
}
