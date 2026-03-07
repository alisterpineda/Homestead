using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TrashTests : IntegrationTestBase
{
    public TrashTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task SoftDelete_DocumentDisappearsFromList()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Disappear.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId}", CT);

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.DoesNotContain(body!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task SoftDelete_DocumentReturns404OnGet()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Gone.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId}", CT);

        var response = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_DeletedFilter_ShowsTrashedDocuments()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "Trash1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "Trash2.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId1}", CT);
        await Task.Delay(50, CT); // ensure distinct DeletedAt timestamps
        await client.DeleteAsync($"/api/v1/documents/{docId2}", CT);

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);

        Assert.Contains(body!.Items, d => d.Id == docId1);
        Assert.Contains(body.Items, d => d.Id == docId2);

        // Default sort is DeletedAt DESC — most recently deleted first
        var idx1 = body.Items.ToList().FindIndex(d => d.Id == docId1);
        var idx2 = body.Items.ToList().FindIndex(d => d.Id == docId2);
        Assert.True(idx2 < idx1, "Most recently deleted should appear first");

        // DeletedAt fields should be populated
        var trashedDoc = body.Items.First(d => d.Id == docId1);
        Assert.NotNull(trashedDoc.DeletedAt);
        Assert.NotNull(trashedDoc.DeletedByUserId);
    }

    [Fact]
    public async Task GetDocuments_DeletedFilter_OnlyShowsAccessibleDocuments()
    {
        var adminClient = await CreateAdminClientAsync();

        var accessibleGroupId = await CreateGroupAsync(adminClient, "TrashAccessible");
        var restrictedGroupId = await CreateGroupAsync(adminClient, "TrashRestricted");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, accessibleGroupId, contributorId, "Editor");

        var accessibleDocId = await UploadAndWaitAsync(contributorClient, "accessible-trash.pdf", accessibleGroupId);
        var restrictedDocId = await UploadAndWaitAsync(adminClient, "restricted-trash.pdf", restrictedGroupId);

        // Delete both
        await contributorClient.DeleteAsync($"/api/v1/documents/{accessibleDocId}", CT);
        await adminClient.DeleteAsync($"/api/v1/documents/{restrictedDocId}", CT);

        // Contributor should only see their accessible doc in trash
        var response = await contributorClient.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Contains(body!.Items, d => d.Id == accessibleDocId);
        Assert.DoesNotContain(body.Items, d => d.Id == restrictedDocId);
    }

    [Fact]
    public async Task GetDocuments_DeletedFilter_EmptyWhenNoDeletedDocs()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Active.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task RestoreDocuments_RestoresFromTrash()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "Restore.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId}", CT);

        // Verify in trash
        var trashResponse = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var trashBody = await trashResponse.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Contains(trashBody!.Items, d => d.Id == docId);

        // Restore
        var restoreResponse = await client.PostAsync($"/api/v1/documents/{docId}/restore", null, CT);
        Assert.Equal(HttpStatusCode.NoContent, restoreResponse.StatusCode);

        // Verify back in active list
        var activeResponse = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false", CT);
        var activeBody = await activeResponse.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Contains(activeBody!.Items, d => d.Id == docId);

        // Verify no longer in trash
        trashResponse = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        trashBody = await trashResponse.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.DoesNotContain(trashBody!.Items, d => d.Id == docId);
    }

    [Fact]
    public async Task RestoreDocuments_InaccessibleDoc_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();
        var restrictedGroupId = await CreateGroupAsync(adminClient, "RestoreRestricted");
        var docId = await UploadAndWaitAsync(adminClient, "restricted-restore.pdf", restrictedGroupId);
        await adminClient.DeleteAsync($"/api/v1/documents/{docId}", CT);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");

        var response = await contributorClient.PostAsync($"/api/v1/documents/{docId}/restore", null, CT);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PermanentlyDelete_RemovesFromTrash()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "PermDelete.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId}", CT);

        var permDeleteResponse = await client.PostAsync($"/api/v1/documents/{docId}/permanently-delete", null, CT);
        Assert.Equal(HttpStatusCode.NoContent, permDeleteResponse.StatusCode);

        // Verify gone from trash
        var trashResponse = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var trashBody = await trashResponse.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.DoesNotContain(trashBody!.Items, d => d.Id == docId);

        // Verify gone from active list
        var activeResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, activeResponse.StatusCode);
    }

    [Fact]
    public async Task PermanentlyDelete_OnlyWorksOnTrashedDocs()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "NotTrashed.pdf");

        // Try to permanently delete a non-trashed doc — should return 403 (inaccessible since not in trash)
        var response = await client.PostAsync($"/api/v1/documents/{docId}/permanently-delete", null, CT);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify doc still exists
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task EmptyTrash_DeletesAllAccessibleTrashedDocs()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "Empty1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "Empty2.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId1}", CT);
        await client.DeleteAsync($"/api/v1/documents/{docId2}", CT);

        var response = await client.PostAsync("/api/v1/documents/empty-trash", null, CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EmptyTrashResult>(CT);
        Assert.Equal(2, body!.DeletedCount);

        // Verify trash is empty
        var trashResponse = await client.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var trashBody = await trashResponse.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Empty(trashBody!.Items);
    }

    [Fact]
    public async Task EmptyTrash_OnlyDeletesWritableDocs()
    {
        var adminClient = await CreateAdminClientAsync();

        var accessibleGroupId = await CreateGroupAsync(adminClient, "EmptyTrashAccessible");
        var restrictedGroupId = await CreateGroupAsync(adminClient, "EmptyTrashRestricted");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, accessibleGroupId, contributorId, "Editor");

        var accessibleDocId = await UploadAndWaitAsync(contributorClient, "empty-accessible.pdf", accessibleGroupId);
        var restrictedDocId = await UploadAndWaitAsync(adminClient, "empty-restricted.pdf", restrictedGroupId);

        await contributorClient.DeleteAsync($"/api/v1/documents/{accessibleDocId}", CT);
        await adminClient.DeleteAsync($"/api/v1/documents/{restrictedDocId}", CT);

        // Contributor empties trash — should only delete their accessible doc
        var response = await contributorClient.PostAsync("/api/v1/documents/empty-trash", null, CT);
        var body = await response.Content.ReadFromJsonAsync<EmptyTrashResult>(CT);
        Assert.Equal(1, body!.DeletedCount);

        // Admin should still see the restricted doc in trash
        var adminTrash = await adminClient.GetAsync("/api/v1/documents?Page=1&PageSize=50&IncludeChildTags=false&Deleted=true", CT);
        var adminTrashBody = await adminTrash.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Contains(adminTrashBody!.Items, d => d.Id == restrictedDocId);
    }

    [Fact]
    public async Task SoftDeletedDoc_CannotBePatchedOrCommented()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "NoPatch.pdf");

        await client.DeleteAsync($"/api/v1/documents/{docId}", CT);

        // Attempt to patch — should return 404
        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"New Name" }
        });
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        // Attempt to comment — should return 404
        var commentResponse = await client.PostAsJsonAsync($"/api/v1/documents/{docId}/comments", new
        {
            text = "Should not work"
        }, CT);
        Assert.Equal(HttpStatusCode.NotFound, commentResponse.StatusCode);
    }

    private record EmptyTrashResult(int DeletedCount);
    private record RestoreResult(int RestoredCount);
}
