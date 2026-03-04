using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TagVisibilityTests : IntegrationTestBase
{
    public TagVisibilityTests(PostgresFixture fixture) : base(fixture) { }

    // --- Visibility tests ---

    [Fact]
    public async Task Admin_SeesAllTags()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupA = await CreateGroupAsync(adminClient, "TagVis Admin GroupA");
        var groupB = await CreateGroupAsync(adminClient, "TagVis Admin GroupB");

        var docA = await UploadAndWaitAsync(adminClient, "admin-tag-a.pdf", groupA);
        var docB = await UploadAndWaitAsync(adminClient, "admin-tag-b.pdf", groupB);

        await AssignTagsAsync(adminClient, docA, ["admin-vis/alpha"]);
        await AssignTagsAsync(adminClient, docB, ["admin-vis/beta"]);

        var response = await adminClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "admin-vis/alpha");
        Assert.Contains(tags!, t => t.Path == "admin-vis/beta");
    }

    [Fact]
    public async Task Contributor_SeesTagsOnGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "contrib-global-tag.pdf");
        await AssignTagsAsync(adminClient, docId, ["global-tag-vis"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "global-tag-vis");
    }

    [Fact]
    public async Task Contributor_SeesTagsOnOwnGroupDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "TagVis Contrib Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        var docId = await UploadAndWaitAsync(adminClient, "contrib-group-tag.pdf", groupId);
        await AssignTagsAsync(adminClient, docId, ["own-group-tag"]);

        var response = await contributorClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "own-group-tag");
    }

    [Fact]
    public async Task Contributor_SeesTagsOnOtherGroupDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "TagVis Other Group");

        var docB = await UploadAndWaitAsync(adminClient, "other-group-tag.pdf", groupB);
        await AssignTagsAsync(adminClient, docB, ["secret-project-tag"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "secret-project-tag");
    }

    [Fact]
    public async Task Viewer_SeesTagsOnGlobalDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "viewer-global-tag.pdf");
        await AssignTagsAsync(adminClient, docId, ["viewer-visible-tag"]);

        var viewerClient = await CreateViewerClientAsync();

        var response = await viewerClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "viewer-visible-tag");
    }

    [Fact]
    public async Task Guest_SeesAllTags()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupA = await CreateGroupAsync(adminClient, "TagVis Guest GroupA");
        var groupB = await CreateGroupAsync(adminClient, "TagVis Guest GroupB");

        var docA = await UploadAndWaitAsync(adminClient, "guest-tag-a.pdf", groupA);
        var docB = await UploadAndWaitAsync(adminClient, "guest-tag-b.pdf", groupB);
        await AssignTagsAsync(adminClient, docA, ["guest-visible-tag"]);
        await AssignTagsAsync(adminClient, docB, ["guest-other-tag"]);

        var guestClient = await CreateGuestClientAsync();
        var guestId = await GetUserIdAsync(guestClient);
        await AddGroupMemberAsync(adminClient, groupA, guestId, "Viewer");

        var response = await guestClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "guest-visible-tag");
        Assert.Contains(tags!, t => t.Path == "guest-other-tag");
    }

    [Fact]
    public async Task OrphanedTag_VisibleToNonAdmin()
    {
        var adminClient = await CreateAdminClientAsync();

        // Create a tag that isn't assigned to any document
        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/documents/tags",
            new { path = "orphaned-standalone-tag" }, CT);
        createResponse.EnsureSuccessStatusCode();

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);

        Assert.Contains(tags!, t => t.Path == "orphaned-standalone-tag");
    }

    // --- Mutation authorization tests ---

    [Fact]
    public async Task Contributor_CanRenameTag_OnlyOnAccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "rename-tag-ok.pdf");
        await AssignTagsAsync(adminClient, docId, ["renameable-tag"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        // Find the tag ID
        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=renameable-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "renameable-tag").Id;

        var response = await SendJsonPatchAsync(contributorClient, $"/api/v1/documents/tags/{tagId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"renamed-tag" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotRenameTag_UsedOnInaccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "TagMut Rename Block Group");

        // Tag used on both a global doc and a group doc the contributor can't access
        var globalDoc = await UploadAndWaitAsync(adminClient, "rename-tag-global.pdf");
        var groupDoc = await UploadAndWaitAsync(adminClient, "rename-tag-group.pdf", groupB);
        await AssignTagsAsync(adminClient, globalDoc, ["shared-rename-tag"]);
        await AssignTagsAsync(adminClient, groupDoc, ["shared-rename-tag"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=shared-rename-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "shared-rename-tag").Id;

        var response = await SendJsonPatchAsync(contributorClient, $"/api/v1/documents/tags/{tagId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"hacked-name" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotRenameParentTag_WhenDescendantOnInaccessibleDoc()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "TagMut Parent Rename Group");

        // Create a parent tag on a global doc (accessible)
        var globalDoc = await UploadAndWaitAsync(adminClient, "parent-rename-global.pdf");
        await AssignTagsAsync(adminClient, globalDoc, ["parent-mut"]);

        // Create a child tag on a group doc (inaccessible to contributor)
        var groupDoc = await UploadAndWaitAsync(adminClient, "parent-rename-group.pdf", groupB);
        await AssignTagsAsync(adminClient, groupDoc, ["parent-mut/child-mut"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=parent-mut&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var parentTagId = tags!.First(t => t.Path == "parent-mut").Id;

        var response = await SendJsonPatchAsync(contributorClient, $"/api/v1/documents/tags/{parentTagId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"parent-renamed" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CanDeleteTag_OnlyOnAccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(adminClient, "delete-tag-ok.pdf");
        await AssignTagsAsync(adminClient, docId, ["deleteable-tag"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=deleteable-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "deleteable-tag").Id;

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/tags/{tagId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotDeleteTag_UsedOnInaccessibleDocs()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "TagMut Delete Block Group");

        var globalDoc = await UploadAndWaitAsync(adminClient, "delete-tag-global.pdf");
        var groupDoc = await UploadAndWaitAsync(adminClient, "delete-tag-group.pdf", groupB);
        await AssignTagsAsync(adminClient, globalDoc, ["shared-delete-tag"]);
        await AssignTagsAsync(adminClient, groupDoc, ["shared-delete-tag"]);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=shared-delete-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "shared-delete-tag").Id;

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/tags/{tagId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAlwaysModifyTags()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupB = await CreateGroupAsync(adminClient, "TagMut Admin Bypass Group");

        var groupDoc = await UploadAndWaitAsync(adminClient, "admin-modify-tag.pdf", groupB);
        await AssignTagsAsync(adminClient, groupDoc, ["admin-bypass-tag"]);

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=admin-bypass-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "admin-bypass-tag").Id;

        var response = await SendJsonPatchAsync(adminClient, $"/api/v1/documents/tags/{tagId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"admin-renamed-tag" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotDeleteTag_OnGroupDocWithViewerAccess()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "TagDel Viewer Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Viewer");

        var docId = await UploadAndWaitAsync(adminClient, "tag-del-viewer.pdf", groupId);
        await AssignTagsAsync(adminClient, docId, ["viewer-delete-tag"]);

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=viewer-delete-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "viewer-delete-tag").Id;

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/tags/{tagId}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CanDeleteTag_OnGroupDocWithEditorAccess()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "TagDel Editor Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId, contributorId, "Editor");

        var docId = await UploadAndWaitAsync(adminClient, "tag-del-editor.pdf", groupId);
        await AssignTagsAsync(adminClient, docId, ["editor-delete-tag"]);

        var tagsResponse = await adminClient.GetAsync("/api/v1/documents/tags?Search=editor-delete-tag&IncludeChildren=false", CT);
        var tags = await tagsResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagId = tags!.First(t => t.Path == "editor-delete-tag").Id;

        var response = await contributorClient.DeleteAsync($"/api/v1/documents/tags/{tagId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Helpers ---

    private async Task AssignTagsAsync(HttpClient client, Guid docId, string[] tagPaths)
    {
        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)tagPaths }
        });
        response.EnsureSuccessStatusCode();
    }

    private record TagResult(Guid Id, string Name, string Path, Guid? ParentId, string? Color);
}
