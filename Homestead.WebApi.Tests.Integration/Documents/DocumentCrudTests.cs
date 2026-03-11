using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class DocumentCrudTests : IntegrationTestBase
{
    public DocumentCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateDocument_UploadsFile_Returns202WithJobId()
    {
        var client = await CreateAdminClientAsync();

        var response = await UploadDocumentAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JobResponse>(CT);
        Assert.NotEqual(Guid.Empty, body!.JobId);
    }

    [Fact]
    public async Task CreateDocument_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await UploadDocumentAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_ReturnsPagedList()
    {
        var client = await CreateAdminClientAsync();
        await UploadAndWaitAsync(client, "Doc1.pdf");
        await UploadAndWaitAsync(client, "Doc2.pdf");

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.True(body!.TotalCount >= 2);
        Assert.True(body.Items.Count >= 2);
    }

    [Fact]
    public async Task GetDocuments_EmptyDb_ReturnsEmptyList()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListItem>>(CT);
        Assert.Equal(0, body!.TotalCount);
        Assert.Empty(body.Items);
    }

    [Fact]
    public async Task GetDocument_ValidId_ReturnsDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "TestDoc.pdf");

        var response = await client.GetAsync($"/api/v1/documents/{docId}", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        Assert.Equal(docId, body!.Id);
        Assert.Equal("TestDoc.pdf", body.Name);
        Assert.Equal("", body.Notes);
    }

    [Fact]
    public async Task GetDocument_InvalidId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"/api/v1/documents/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchDocument_UpdatesName()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"Updated Name" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        Assert.Equal("Updated Name", body!.Name);
    }

    [Fact]
    public async Task PatchDocument_UpdatesNotes()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/notes", value = (object)"Some important notes" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        Assert.Equal("Some important notes", body!.Notes);
    }

    [Fact]
    public async Task DeleteDocument_RemovesDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task BulkDeleteDocuments_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "Bulk1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "Bulk2.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-delete", new
        {
            ids = new[] { docId1, docId2 }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    [Fact]
    public async Task BulkDeleteDocuments_WithInaccessibleDoc_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();

        // Create two groups — contributor is a member of one but not the other
        var accessibleGroupId = await CreateGroupAsync(adminClient, "Accessible Group");
        var restrictedGroupId = await CreateGroupAsync(adminClient, "Restricted Group");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId: accessibleGroupId, contributorId, "Editor");

        var accessibleDocId = await UploadAndWaitAsync(contributorClient, "accessible.pdf", accessibleGroupId);
        var restrictedDocId = await UploadAndWaitAsync(adminClient, "restricted.pdf", restrictedGroupId);

        // Bulk delete both — should fail with 403 because one is inaccessible
        var response = await contributorClient.PostAsJsonAsync("/api/v1/documents/bulk-delete", new
        {
            ids = new[] { accessibleDocId, restrictedDocId }
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify neither document was deleted
        var checkAccessible = await adminClient.GetAsync($"/api/v1/documents/{accessibleDocId}", CT);
        Assert.Equal(HttpStatusCode.OK, checkAccessible.StatusCode);

        var checkRestricted = await adminClient.GetAsync($"/api/v1/documents/{restrictedDocId}", CT);
        Assert.Equal(HttpStatusCode.OK, checkRestricted.StatusCode);
    }

    [Fact]
    public async Task BulkUpdateDocuments_AddsTags()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "BulkTag1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "BulkTag2.pdf");

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = new[] { docId1, docId2 },
            tagPaths = new[] { "finance", "invoices" },
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkUpdateResult>(CT);
        Assert.Equal(2, body!.UpdatedCount);

        // Verify tags on each document
        var doc1 = await client.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{docId1}", CT);
        Assert.Equal(2, doc1!.Tags.Count);
        Assert.Contains(doc1.Tags, t => t.Path == "finance");
        Assert.Contains(doc1.Tags, t => t.Path == "invoices");

        var doc2 = await client.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{docId2}", CT);
        Assert.Equal(2, doc2!.Tags.Count);
    }

    [Fact]
    public async Task BulkUpdateDocuments_AddsParties()
    {
        var client = await CreateAdminClientAsync();
        var docId1 = await UploadAndWaitAsync(client, "BulkParty1.pdf");
        var docId2 = await UploadAndWaitAsync(client, "BulkParty2.pdf");

        // Create a party
        var partyResponse = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name = "Test Corp" }, CT);
        var party = await partyResponse.Content.ReadFromJsonAsync<PartyItem>(CT);

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = new[] { docId1, docId2 },
            partyIds = new[] { party!.Id },
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkUpdateResult>(CT);
        Assert.Equal(2, body!.UpdatedCount);

        // Verify party on each document
        var doc1 = await client.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{docId1}", CT);
        Assert.Single(doc1!.Parties);
        Assert.Equal("Test Corp", doc1.Parties[0].Name);
    }

    [Fact]
    public async Task BulkUpdateDocuments_Additive_DoesNotRemoveExisting()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "AdditiveTag.pdf");

        // Patch document with tag A
        await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "tagA" } }
        });

        // Bulk update with tag B
        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = new[] { docId },
            tagPaths = new[] { "tagB" },
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify both tags are present
        var doc = await client.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{docId}", CT);
        Assert.Equal(2, doc!.Tags.Count);
        Assert.Contains(doc.Tags, t => t.Path == "tagA");
        Assert.Contains(doc.Tags, t => t.Path == "tagB");
    }

    [Fact]
    public async Task BulkUpdateDocuments_NoDuplicates()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client, "NoDupTag.pdf");

        // Patch document with tag A
        await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "tagA" } }
        });

        // Bulk update with the same tag A
        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = new[] { docId },
            tagPaths = new[] { "tagA" },
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkUpdateResult>(CT);
        Assert.Equal(0, body!.UpdatedCount); // No changes made

        // Verify only one instance of tag A
        var doc = await client.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{docId}", CT);
        Assert.Single(doc!.Tags);
        Assert.Equal("tagA", doc.Tags[0].Path);
    }

    [Fact]
    public async Task BulkUpdateDocuments_WithInaccessibleDoc_Returns403()
    {
        var adminClient = await CreateAdminClientAsync();

        // Create two groups — contributor is a member of one but not the other
        var accessibleGroupId = await CreateGroupAsync(adminClient, "BulkUpdate Accessible");
        var restrictedGroupId = await CreateGroupAsync(adminClient, "BulkUpdate Restricted");

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var contributorId = await GetUserIdAsync(contributorClient);
        await AddGroupMemberAsync(adminClient, groupId: accessibleGroupId, contributorId, "Editor");

        var accessibleDocId = await UploadAndWaitAsync(contributorClient, "bu-accessible.pdf", accessibleGroupId);
        var restrictedDocId = await UploadAndWaitAsync(adminClient, "bu-restricted.pdf", restrictedGroupId);

        var response = await contributorClient.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = new[] { accessibleDocId, restrictedDocId },
            tagPaths = new[] { "test-tag" },
        }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify neither document was modified
        var checkAccessible = await adminClient.GetFromJsonAsync<DocumentDetailWithMeta>($"/api/v1/documents/{accessibleDocId}", CT);
        Assert.Empty(checkAccessible!.Tags);
    }

    [Fact]
    public async Task BulkUpdateDocuments_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/bulk-update", new
        {
            ids = Array.Empty<Guid>(),
            tagPaths = new[] { "some-tag" },
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadDocumentAsync(HttpClient client, string name = "sample.pdf")
    {
        var fileBytes = await GetUniquePdfBytesAsync();
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new ByteArrayContent(fileBytes), "file", name);

        return await client.PostAsync("/api/v1/documents", form);
    }

    private record DocumentListItem(Guid Id, string Name, string ContentType, long FileSize, string FileExtension);
    private record DocumentDetail(Guid Id, string Name, string ContentType, long FileSize, string FileExtension, DateTimeOffset CreatedAt, string? Notes = null);
    private record DocumentDetailWithMeta(Guid Id, string Name, string ContentType, long FileSize, string FileExtension, DateTimeOffset CreatedAt, string? Notes, IReadOnlyList<TagItem> Tags, IReadOnlyList<PartyItem> Parties);
    private record TagItem(Guid Id, string Name, string Path);
    private record PartyItem(Guid Id, string Name);
}
