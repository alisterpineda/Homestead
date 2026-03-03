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

    // --- Version operation access checks ---

    [Fact]
    public async Task ContributorAsGroupViewer_CannotDeleteVersion()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "VersionDelete Viewer Group");
        var docId = await UploadAndWaitAsync(adminClient, "version-delete.pdf", groupId);

        // Add a second version so deletion is allowed
        await UploadVersionAsync(adminClient, docId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var versionId = await GetFirstVersionIdAsync(adminClient, docId);
        var response = await contributorClient.DeleteAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ContributorAsGroupViewer_CannotPatchVersion()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "VersionPatch Viewer Group");
        var docId = await UploadAndWaitAsync(adminClient, "version-patch.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var versionId = await GetFirstVersionIdAsync(adminClient, docId);
        var response = await SendJsonPatchAsync(contributorClient,
            $"/api/v1/documents/{docId}/versions/{versionId}", new[]
            {
                new { op = "replace", path = "/label", value = (object)"Hacked" }
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ContributorAsGroupEditor_CanDeleteVersion()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "VersionDelete Editor Group");
        var docId = await UploadAndWaitAsync(adminClient, "version-delete-ok.pdf", groupId);

        // Add a second version so deletion is allowed
        await UploadVersionAsync(adminClient, docId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        // Delete the first version (not the last)
        var versionId = await GetFirstVersionIdAsync(adminClient, docId);
        var response = await contributorClient.DeleteAsync($"/api/v1/documents/{docId}/versions/{versionId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Activity endpoint access checks ---

    [Fact]
    public async Task NonMember_CannotReadGroupDocActivity()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Activity Restricted Group");
        var docId = await UploadAndWaitAsync(adminClient, "activity-restricted.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync($"/api/v1/documents/{docId}/activity?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GroupMember_CanReadGroupDocActivity()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Activity Allowed Group");
        var docId = await UploadAndWaitAsync(adminClient, "activity-allowed.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var response = await contributorClient.GetAsync($"/api/v1/documents/{docId}/activity?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Group membership required for create/move ---

    [Fact]
    public async Task NonMember_CannotCreateDocumentInGroup()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Create Restricted Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("blocked-upload.pdf"), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", "blocked-upload.pdf");
        form.Add(new StringContent(groupId.ToString()), "groupId");

        var response = await contributorClient.PostAsync("/api/v1/documents", form, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GroupEditor_CanCreateDocumentInGroup()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Create Allowed Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("allowed-upload.pdf"), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", "allowed-upload.pdf");
        form.Add(new StringContent(groupId.ToString()), "groupId");

        var response = await contributorClient.PostAsync("/api/v1/documents", form, CT);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotMoveDocumentToGroup()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Move Restricted Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var docId = await UploadAndWaitAsync(contributorClient, "move-blocked.pdf");

        var response = await SendJsonPatchAsync(contributorClient, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/groupId", value = (object)groupId.ToString() }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GroupEditor_CanMoveDocumentToGroup()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Move Allowed Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        var docId = await UploadAndWaitAsync(contributorClient, "move-allowed.pdf");

        var response = await SendJsonPatchAsync(contributorClient, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/groupId", value = (object)groupId.ToString() }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Comment access checks ---

    [Fact]
    public async Task ContributorAsGroupViewer_CannotCommentOnGroupDoc()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Comment Viewer Group");
        var docId = await UploadAndWaitAsync(adminClient, "comment-viewer.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var response = await contributorClient.PostAsJsonAsync(
            $"/api/v1/documents/{docId}/comments", new { body = "Should be denied" }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Delete access checks ---

    [Fact]
    public async Task NonMember_CannotDeleteGroupDocument()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Delete Restricted Group");
        var docId = await UploadAndWaitAsync(adminClient, "delete-restricted.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/{docId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ContributorAsGroupViewer_CannotDeleteGroupDoc()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Delete Viewer Group");
        var docId = await UploadAndWaitAsync(adminClient, "delete-viewer.pdf", groupId);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/{docId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Helper methods ---

    private async Task UploadVersionAsync(HttpClient client, Guid docId)
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("V2"), "label");
        form.Add(new ByteArrayContent(fileBytes), "file", "sample-v2.pdf");

        var response = await client.PostAsync($"/api/v1/documents/{docId}/versions", form, CT);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var jobBody = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        await WaitForJobCompletionAsync(jobBody!.JobId);
    }

    private async Task<Guid> GetFirstVersionIdAsync(HttpClient client, Guid docId)
    {
        var response = await client.GetAsync($"/api/v1/documents/{docId}/versions?Page=1&PageSize=10", CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<VersionItem>>(CT);
        return body!.Items.OrderBy(v => v.VersionNumber).First().Id;
    }

    private record VersionItem(Guid Id, int VersionNumber, string? Label);
}
