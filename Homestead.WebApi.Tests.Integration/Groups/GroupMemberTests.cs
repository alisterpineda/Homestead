using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Groups;

[Collection("Integration")]
public class GroupMemberTests : IntegrationTestBase
{
    public GroupMemberTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanAddMember()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Member Test Group");

        // Create a target user
        var targetClient = await CreateUserClientAsync(role: "Contributor");
        var targetUserId = await GetUserIdAsync(targetClient);

        await AddGroupMemberAsync(adminClient, groupId, targetUserId, "Editor");

        // Verify member was added
        var response = await adminClient.GetAsync($"/api/v1/admin/groups/{groupId}", CT);
        var body = await response.Content.ReadFromJsonAsync<GroupDetail>(CT);
        Assert.Equal(2, body!.Members.Count); // Admin (Owner) + target (Editor)
        Assert.Contains(body.Members, m => m.UserId == targetUserId && m.AccessLevel == "Editor");
    }

    [Fact]
    public async Task AddDuplicateMember_Returns409()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Dup Member Group");

        var targetClient = await CreateUserClientAsync(role: "Viewer");
        var targetUserId = await GetUserIdAsync(targetClient);

        await AddGroupMemberAsync(adminClient, groupId, targetUserId, "Viewer");

        // Try to add same user again
        var response = await adminClient.PostAsJsonAsync($"/api/v1/admin/groups/{groupId}/members", new
        {
            userId = targetUserId,
            accessLevel = "Editor"
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRemoveMember()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Remove Member Group");

        var targetClient = await CreateUserClientAsync(role: "Contributor");
        var targetUserId = await GetUserIdAsync(targetClient);

        await AddGroupMemberAsync(adminClient, groupId, targetUserId, "Editor");

        var response = await adminClient.DeleteAsync($"/api/v1/admin/groups/{groupId}/members/{targetUserId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify member was removed
        var getResponse = await adminClient.GetAsync($"/api/v1/admin/groups/{groupId}", CT);
        var body = await getResponse.Content.ReadFromJsonAsync<GroupDetail>(CT);
        Assert.DoesNotContain(body!.Members, m => m.UserId == targetUserId);
    }

    [Fact]
    public async Task RemoveNonexistentMember_Returns404()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Nonexistent Member Group");

        var response = await adminClient.DeleteAsync($"/api/v1/admin/groups/{groupId}/members/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanUpdateMemberAccessLevel()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Update Level Group");

        var targetClient = await CreateUserClientAsync(role: "Contributor");
        var targetUserId = await GetUserIdAsync(targetClient);

        await AddGroupMemberAsync(adminClient, groupId, targetUserId, "Viewer");

        var response = await adminClient.PutAsJsonAsync($"/api/v1/admin/groups/{groupId}/members/{targetUserId}", new
        {
            accessLevel = "Owner"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GroupMemberItem>(CT);
        Assert.Equal("Owner", body!.AccessLevel);
    }
}
