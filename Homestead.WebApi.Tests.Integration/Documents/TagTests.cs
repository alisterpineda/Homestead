using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TagTests : IntegrationTestBase
{
    public TagTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateTag_ReturnsCreated()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "Legal",
            color = "#FF0000"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("legal", body!.Name);
        Assert.Equal("legal", body.Path);
        Assert.Equal("#FF0000", body.Color);
    }

    [Fact]
    public async Task CreateTag_WithParent_SetsHierarchy()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents" }, CT);
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents/Contracts" }, CT);
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        Assert.Equal("contracts", child!.Name);
        Assert.Equal("documents/contracts", child.Path);
        Assert.Equal(parent!.Id, child.ParentId);
    }

    [Fact]
    public async Task GetTags_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagA" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagB" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.True(body!.Count >= 2);
    }

    [Fact]
    public async Task GetTags_SearchFilter_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UniqueSearchable" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OtherTag" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Search=uniquesearchable", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.Single(body!, t => t.Name == "uniquesearchable");
    }

    [Fact]
    public async Task PatchTag_UpdatesName()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OldName" }, CT);
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{tag!.Id}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"NewName" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Contains(body!.Name, new[] { "NewName", "newname" });
    }

    [Fact]
    public async Task PatchTag_CircularParent_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent" }, CT);
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent/Child" }, CT);
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{parent!.Id}", new[]
        {
            new { op = "replace", path = "/parentId", value = (object)child!.Id.ToString() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTag_Removes()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Deletable" }, CT);
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/tags/{tag!.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await listResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.DoesNotContain(tags!, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task BulkDeleteTags_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();

        var r1 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkA" }, CT);
        var t1 = await r1.Content.ReadFromJsonAsync<TagResult>(CT);
        var r2 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkB" }, CT);
        var t2 = await r2.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { t1!.Id, t2!.Id }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    [Fact]
    public async Task BulkDeleteTag_ReparentsChildrenToGrandparent()
    {
        var client = await CreateAdminClientAsync();

        // Create A/B/C
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RpA" }, CT);
        var bResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RpA/RpB" }, CT);
        var b = await bResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RpA/RpB/RpC" }, CT);

        // Delete B
        var deleteResp = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { b!.Id }
        }, CT);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        // C should now be re-parented under A with path "rpa/rpc"
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var c = tags!.Single(t => t.Name == "rpc");
        var a = tags.Single(t => t.Name == "rpa");
        Assert.Equal(a.Id, c.ParentId);
        Assert.Equal("rpa/rpc", c.Path);
    }

    [Fact]
    public async Task BulkDeleteTag_RootDeletion_ChildrenBecomeRoots()
    {
        var client = await CreateAdminClientAsync();

        // Create A with children B and C
        var aResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RootDel" }, CT);
        var a = await aResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RootDel/ChildB" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "RootDel/ChildC" }, CT);

        // Delete root A
        var deleteResp = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { a!.Id }
        }, CT);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        // B and C should now be roots
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var childB = tags!.Single(t => t.Name == "childb");
        var childC = tags.Single(t => t.Name == "childc");
        Assert.Null(childB.ParentId);
        Assert.Equal("childb", childB.Path);
        Assert.Null(childC.ParentId);
        Assert.Equal("childc", childC.Path);
    }

    [Fact]
    public async Task BulkDeleteTags_MultipleInHierarchy_ProcessesCorrectly()
    {
        var client = await CreateAdminClientAsync();

        // Create A/B/C/D
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "MhA" }, CT);
        var bResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "MhA/MhB" }, CT);
        var b = await bResp.Content.ReadFromJsonAsync<TagResult>(CT);
        var cResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "MhA/MhB/MhC" }, CT);
        var c = await cResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "MhA/MhB/MhC/MhD" }, CT);

        // Delete B and C
        var deleteResp = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { b!.Id, c!.Id }
        }, CT);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        // D should be re-parented under A
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var d = tags!.Single(t => t.Name == "mhd");
        var a = tags.Single(t => t.Name == "mha");
        Assert.Equal(a.Id, d.ParentId);
        Assert.Equal("mha/mhd", d.Path);
    }

    [Fact]
    public async Task BulkDeleteTag_UpdatesAllDescendantPaths()
    {
        var client = await CreateAdminClientAsync();

        // Create A/B/C, A/B/D, A/B/C/E
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DpA" }, CT);
        var bResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DpA/DpB" }, CT);
        var b = await bResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DpA/DpB/DpC" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DpA/DpB/DpD" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DpA/DpB/DpC/DpE" }, CT);

        // Delete B
        var deleteResp = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { b!.Id }
        }, CT);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        // Verify all paths updated
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        var tagC = tags!.Single(t => t.Name == "dpc");
        var tagD = tags.Single(t => t.Name == "dpd");
        var tagE = tags.Single(t => t.Name == "dpe");

        Assert.Equal("dpa/dpc", tagC.Path);
        Assert.Equal("dpa/dpd", tagD.Path);
        Assert.Equal("dpa/dpc/dpe", tagE.Path);
    }

    private record TagResult(Guid Id, string Name, string Path, Guid? ParentId, string? Color);
}
