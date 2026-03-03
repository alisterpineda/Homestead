using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Groups;

[Collection("Integration")]
public class GroupDocumentAccessTests : IntegrationTestBase
{
    public GroupDocumentAccessTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanAccessAllDocuments()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Admin Access Group");

        // Upload global doc and group-scoped doc
        await UploadAndWaitAsync(adminClient, "global.pdf");
        await UploadAndWaitAsync(adminClient, "grouped.pdf", groupId);

        var response = await adminClient.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);

        Assert.True(body!.TotalCount >= 2);
    }

    [Fact]
    public async Task Contributor_CanReadGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "contributor-global.pdf");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanReadGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "viewer-global.pdf");

        var viewerClient = await CreateViewerClientAsync();

        var response = await viewerClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Guest_CannotAccessGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "guest-global.pdf");

        var guestClient = await CreateGuestClientAsync();

        var response = await guestClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotAccessGroupDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Restricted Group");
        var docId = await UploadAndWaitAsync(adminClient, "restricted.pdf", groupId);

        var outsiderClient = await CreateUserClientAsync(role: "Contributor");

        var response = await outsiderClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GroupMember_CanAccessGroupDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Member Access Group");
        var docId = await UploadAndWaitAsync(adminClient, "member-doc.pdf", groupId);

        var memberClient = await CreateUserClientAsync(role: "Contributor");
        var memberId = await GetUserIdAsync(memberClient);
        await AddGroupMemberAsync(adminClient, groupId, memberId, "Editor");

        var response = await memberClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Guest_WithGroupMembership_CanReadGroupDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Guest Member Group");
        var docId = await UploadAndWaitAsync(adminClient, "guest-group-doc.pdf", groupId);

        var guestClient = await CreateGuestClientAsync();
        var guestId = await GetUserIdAsync(guestClient);
        await AddGroupMemberAsync(adminClient, groupId, guestId, "Editor");

        var response = await guestClient.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DocumentList_OnlyShowsVisibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Visibility Test Group");

        await UploadAndWaitAsync(adminClient, "visible-global.pdf");
        await UploadAndWaitAsync(adminClient, "visible-group.pdf", groupId);

        // A contributor who is NOT a member should only see global doc
        var outsiderClient = await CreateUserClientAsync(role: "Contributor");

        var response = await outsiderClient.GetAsync("/api/v1/documents?Page=1&PageSize=100&IncludeChildTags=false", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);

        Assert.Contains(body!.Items, d => d.Name == "visible-global.pdf");
        Assert.DoesNotContain(body.Items, d => d.Name == "visible-group.pdf");
    }

    [Fact]
    public async Task UploadWithGroupId_ScopesDocumentToGroup()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Upload Scope Group");

        var docId = await UploadAndWaitAsync(adminClient, "scoped-upload.pdf", groupId);

        var response = await adminClient.GetAsync($"/api/v1/documents/{docId}", CT);
        var body = await response.Content.ReadFromJsonAsync<DocumentListItem>(CT);

        Assert.Equal(groupId, body!.GroupId);
        Assert.Equal("Upload Scope Group", body.GroupName);
    }

    [Fact]
    public async Task PatchDocument_CanChangeGroupId()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Patch Group");

        var docId = await UploadAndWaitAsync(adminClient, "patch-group.pdf");

        // Assign document to group
        var patchResponse = await SendJsonPatchAsync(adminClient, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/groupId", value = (object)groupId.ToString() }
        });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // Verify
        var getResponse = await adminClient.GetAsync($"/api/v1/documents/{docId}", CT);
        var body = await getResponse.Content.ReadFromJsonAsync<DocumentListItem>(CT);
        Assert.Equal(groupId, body!.GroupId);
    }
}
