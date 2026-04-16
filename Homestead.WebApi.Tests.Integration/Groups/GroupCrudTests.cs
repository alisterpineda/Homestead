using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Groups;

[Collection("Integration")]
public class GroupCrudTests : IntegrationTestBase
{
    public GroupCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanCreateGroup()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/groups", new
        {
            name = "Test Group",
            description = "A test group"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GroupItem>(CT);
        Assert.Equal("Test Group", body!.Name);
        Assert.Equal(1, body.MemberCount); // Creator auto-added as Owner
    }

    [Fact]
    public async Task Admin_CanListGroups()
    {
        var client = await CreateAdminClientAsync();
        await CreateGroupAsync(client, "Group A");
        await CreateGroupAsync(client, "Group B");

        var response = await client.GetAsync("/api/v1/admin/groups?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<GroupItem>>(CT);
        Assert.True(body!.TotalCount >= 2);
    }

    [Fact]
    public async Task Admin_CanGetGroup()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Detail Group");

        var response = await client.GetAsync($"/api/v1/admin/groups/{groupId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GroupDetail>(CT);
        Assert.Equal("Detail Group", body!.Name);
        Assert.Single(body.Members); // Creator is auto-added
        Assert.Equal("Owner", body.Members[0].AccessLevel);
    }

    [Fact]
    public async Task Admin_CanUpdateGroup()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Old Name");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/groups/{groupId}", new
        {
            name = "New Name",
            description = "Updated desc"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GroupItem>(CT);
        Assert.Equal("New Name", body!.Name);
        Assert.NotEqual(default, body.UpdatedAt);
        Assert.NotNull(body.UpdatedByUserId);
    }

    [Fact]
    public async Task Admin_CanDeleteGroup()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Delete Me");

        var response = await client.DeleteAsync($"/api/v1/admin/groups/{groupId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/admin/groups/{groupId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_DuplicateName_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateGroupAsync(client, "Unique Name");

        var response = await client.PostAsJsonAsync("/api/v1/admin/groups", new
        {
            name = "Unique Name"
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_SetsDocumentGroupIdToNull()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Docs Group");

        // Upload a document scoped to the group
        var docId = await UploadAndWaitAsync(client, "grouped-doc.pdf", groupId);

        // Verify document is in group
        var docResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, docResponse.StatusCode);
        var doc = await docResponse.Content.ReadFromJsonAsync<DocumentListItem>(CT);
        Assert.Equal(groupId, doc!.GroupId);

        // Delete the group
        var deleteResponse = await client.DeleteAsync($"/api/v1/admin/groups/{groupId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify document GroupId is now null
        var docAfter = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        var docBody = await docAfter.Content.ReadFromJsonAsync<DocumentListItem>(CT);
        Assert.Null(docBody!.GroupId);
    }

    [Fact]
    public async Task NonAdmin_Gets403OnGroupEndpoints()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/admin/groups?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanSearchGroups()
    {
        var client = await CreateAdminClientAsync();
        await CreateGroupAsync(client, "Alpha Team");
        await CreateGroupAsync(client, "Beta Team");

        var response = await client.GetAsync("/api/v1/admin/groups?Page=1&PageSize=10&Search=alpha", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<GroupItem>>(CT);
        Assert.Single(body!.Items);
        Assert.Equal("Alpha Team", body.Items[0].Name);
    }

    [Fact]
    public async Task CreateGroup_NameTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/groups", new
        {
            name = new string('a', 257)
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_DescriptionTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/groups", new
        {
            name = "Valid Name",
            description = new string('a', 1025)
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_NameTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Original Name");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/groups/{groupId}", new
        {
            name = new string('a', 257)
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_DescriptionTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(client, "Desc Test Group");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/groups/{groupId}", new
        {
            name = "Desc Test Group",
            description = new string('a', 1025)
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
