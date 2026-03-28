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
        Assert.Equal("Legal", body!.Name);
        Assert.Equal("Legal", body.Path);
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

        Assert.Equal("Contracts", child!.Name);
        Assert.Equal("Documents/Contracts", child.Path);
        Assert.Equal(parent!.Id, child.ParentId);
    }

    [Fact]
    public async Task GetTags_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagA" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagB" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        Assert.True(body!.Items.Count >= 2);
    }

    [Fact]
    public async Task GetTags_SearchFilter_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UniqueSearchable" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OtherTag" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Search=uniquesearchable&Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        Assert.Single(body!.Items, t => t.Name == "UniqueSearchable");
    }

    [Fact]
    public async Task GetTags_SortByNameAsc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SortC" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SortA" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SortB" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?Page=1&PageSize=10&SortBy=Name&SortDir=Asc&Search=Sort", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetTags_SortByNameDesc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SdC" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SdA" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SdB" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?Page=1&PageSize=10&SortBy=Name&SortDir=Desc&Search=Sd", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal(names.OrderByDescending(n => n), names);
    }

    [Fact]
    public async Task GetTags_SortByCreatedAtDesc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TcFirst" }, CT);
        await Task.Delay(50, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TcSecond" }, CT);
        await Task.Delay(50, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TcThird" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?Page=1&PageSize=10&SortBy=CreatedAt&SortDir=Desc&Search=Tc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal("TcThird", names[0]);
        Assert.Equal("TcSecond", names[1]);
        Assert.Equal("TcFirst", names[2]);
    }

    [Fact]
    public async Task GetTags_DefaultSort_IsByNameAsc()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DsZulu" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DsAlpha" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?Page=1&PageSize=10&Search=Ds", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetTags_SortByDocumentCountDesc_ReturnsMostDocsFirst()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DcMany" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DcOne" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "DcNone" }, CT);

        // Assign 2 documents to DcMany
        var doc1 = await UploadAndWaitAsync(client, "tag-sort-1.pdf");
        var doc2 = await UploadAndWaitAsync(client, "tag-sort-2.pdf");
        await AssignTagAsync(client, doc1, "DcMany");
        await AssignTagAsync(client, doc2, "DcMany");

        // Assign 1 document to DcOne
        await AssignTagAsync(client, doc1, "DcOne");

        var response = await client.GetAsync(
            "/api/v1/documents/tags?Page=1&PageSize=10&SortBy=DocumentCount&SortDir=Desc&Search=Dc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var items = body!.Items;
        Assert.Equal("DcMany", items[0].Name);
        Assert.Equal("DcNone", items[^1].Name);
    }

    [Fact]
    public async Task GetTags_SearchWithSort_FiltersAndSorts()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SwZeta" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "SwAlpha" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Other" }, CT);

        var response = await client.GetAsync(
            "/api/v1/documents/tags?Page=1&PageSize=10&Search=Sw&SortBy=Name&SortDir=Asc", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.Equal("SwAlpha", names[0]);
        Assert.Equal("SwZeta", names[1]);
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
        Assert.Equal("NewName", body!.Name);
    }

    [Fact]
    public async Task PatchTag_SetsUpdatedAtAndUpdatedByUserId()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UpdAtTag" }, CT);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Null(created!.UpdatedAt);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{created.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object)"modified" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.NotNull(body!.UpdatedAt);
        Assert.NotNull(body.UpdatedByUserId);
    }

    [Fact]
    public async Task GetTags_SortByUpdatedAtDesc_ReturnsSorted()
    {
        var client = await CreateAdminClientAsync();
        var r1 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UaFirst" }, CT);
        var t1 = await r1.Content.ReadFromJsonAsync<TagResult>(CT);
        var r2 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UaSecond" }, CT);
        var t2 = await r2.Content.ReadFromJsonAsync<TagResult>(CT);
        var r3 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UaThird" }, CT);
        var t3 = await r3.Content.ReadFromJsonAsync<TagResult>(CT);

        // Update them in order with delays to ensure distinct timestamps
        await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{t1!.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object)"first update" }
        });
        await Task.Delay(50, CT);
        await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{t2!.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object)"second update" }
        });
        await Task.Delay(50, CT);
        await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{t3!.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object)"third update" }
        });

        var response = await client.GetAsync("/api/v1/documents/tags?Page=1&PageSize=10&SortBy=UpdatedAt&SortDir=Desc&Search=Ua", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TagListItem>>(CT);
        var names = body!.Items.Select(t => t.Name).ToList();
        Assert.Equal("UaThird", names[0]);
        Assert.Equal("UaSecond", names[1]);
        Assert.Equal("UaFirst", names[2]);
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

        var listResponse = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        Assert.DoesNotContain(tags!.Items, t => t.Id == tag.Id);
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
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var c = tags!.Items.Single(t => t.Name == "RpC");
        var a = tags.Items.Single(t => t.Name == "RpA");
        Assert.Equal(a.Id, c.ParentId);
        Assert.Equal("RpA/RpC", c.Path);
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
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var childB = tags!.Items.Single(t => t.Name == "ChildB");
        var childC = tags.Items.Single(t => t.Name == "ChildC");
        Assert.Null(childB.ParentId);
        Assert.Equal("ChildB", childB.Path);
        Assert.Null(childC.ParentId);
        Assert.Equal("ChildC", childC.Path);
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
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var d = tags!.Items.Single(t => t.Name == "MhD");
        var a = tags.Items.Single(t => t.Name == "MhA");
        Assert.Equal(a.Id, d.ParentId);
        Assert.Equal("MhA/MhD", d.Path);
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
        var tagsResp = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var tagC = tags!.Items.Single(t => t.Name == "DpC");
        var tagD = tags.Items.Single(t => t.Name == "DpD");
        var tagE = tags.Items.Single(t => t.Name == "DpE");

        Assert.Equal("DpA/DpC", tagC.Path);
        Assert.Equal("DpA/DpD", tagD.Path);
        Assert.Equal("DpA/DpC/DpE", tagE.Path);
    }

    [Fact]
    public async Task CreateTag_WithColor_SetsEffectiveColor()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "EcTest",
            color = "#3b82f6"
        }, CT);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("#3b82f6", body!.Color);
        Assert.Equal("#3b82f6", body.EffectiveColor);
    }

    [Fact]
    public async Task CreateTag_ChildInheritsParentColor()
    {
        var client = await CreateAdminClientAsync();

        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcParent", color = "#3b82f6" }, CT);

        var childResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcParent/EcChild" }, CT);
        childResp.EnsureSuccessStatusCode();
        var child = await childResp.Content.ReadFromJsonAsync<TagResult>(CT);

        Assert.Null(child!.Color);
        Assert.Equal("#3b82f6", child.EffectiveColor);
    }

    [Fact]
    public async Task CreateTag_DeepInheritance()
    {
        var client = await CreateAdminClientAsync();

        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDeep", color = "#3b82f6" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDeep/Mid" }, CT);
        var leafResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDeep/Mid/Leaf" }, CT);
        leafResp.EnsureSuccessStatusCode();
        var leaf = await leafResp.Content.ReadFromJsonAsync<TagResult>(CT);

        Assert.Null(leaf!.Color);
        Assert.Equal("#3b82f6", leaf.EffectiveColor);
    }

    [Fact]
    public async Task PatchTag_ChangeColor_CascadesToDescendants()
    {
        var client = await CreateAdminClientAsync();

        var parentResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcCascade" }, CT);
        var parent = await parentResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcCascade/Child" }, CT);

        // Set parent color — should cascade to child
        await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{parent!.Id}", new[]
        {
            new { op = "replace", path = "/color", value = (object)"#ef4444" }
        });

        var tagsResp = await client.GetAsync("/api/v1/documents/tags?Search=EcCascade&IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var child = tags!.Items.Single(t => t.Name == "Child");

        Assert.Equal("#ef4444", child.EffectiveColor);
    }

    [Fact]
    public async Task PatchTag_ClearColor_DescendantInheritsGrandparent()
    {
        var client = await CreateAdminClientAsync();

        var gpResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcGp", color = "#3b82f6" }, CT);
        var gp = await gpResp.Content.ReadFromJsonAsync<TagResult>(CT);
        var pResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcGp/EcMid", color = "#ef4444" }, CT);
        var p = await pResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcGp/EcMid/EcLeaf" }, CT);

        // Clear mid's color — leaf should now inherit grandparent's color
        await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{p!.Id}", new[]
        {
            new { op = "replace", path = "/color", value = (object?)null! }
        });

        var tagsResp = await client.GetAsync("/api/v1/documents/tags?Search=EcGp&IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var leaf = tags!.Items.Single(t => t.Name == "EcLeaf");
        var mid = tags.Items.Single(t => t.Name == "EcMid");

        Assert.Equal("#3b82f6", mid.EffectiveColor);
        Assert.Equal("#3b82f6", leaf.EffectiveColor);
    }

    [Fact]
    public async Task PatchTag_ChangeParent_UpdatesEffectiveColor()
    {
        var client = await CreateAdminClientAsync();

        var blueResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcBlue", color = "#3b82f6" }, CT);
        var blue = await blueResp.Content.ReadFromJsonAsync<TagResult>(CT);
        var redResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcRed", color = "#ef4444" }, CT);
        var red = await redResp.Content.ReadFromJsonAsync<TagResult>(CT);

        // Create child under blue
        var childResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcBlue/Movable" }, CT);
        var child = await childResp.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("#3b82f6", child!.EffectiveColor);

        // Move child under red
        var patchResp = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{child.Id}", new[]
        {
            new { op = "replace", path = "/parentId", value = (object)red!.Id.ToString() }
        });
        var updated = await patchResp.Content.ReadFromJsonAsync<TagResult>(CT);

        Assert.Equal("#ef4444", updated!.EffectiveColor);
    }

    [Fact]
    public async Task BulkDeleteTag_Reparent_UpdatesEffectiveColor()
    {
        var client = await CreateAdminClientAsync();

        // Create grandparent (blue) / parent (red) / child (no color)
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDel", color = "#3b82f6" }, CT);
        var parentResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDel/EcDelMid", color = "#ef4444" }, CT);
        var parent = await parentResp.Content.ReadFromJsonAsync<TagResult>(CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "EcDel/EcDelMid/EcDelLeaf" }, CT);

        // Delete mid — leaf reparents to grandparent, should inherit blue
        await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { parent!.Id }
        }, CT);

        var tagsResp = await client.GetAsync("/api/v1/documents/tags?Search=EcDel&IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await tagsResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var leaf = tags!.Items.Single(t => t.Name == "EcDelLeaf");

        Assert.Equal("#3b82f6", leaf.EffectiveColor);
    }

    [Fact]
    public async Task CreateTag_WithDescription_ReturnsDescription()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "DescTag",
            description = "A tag for testing descriptions"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("A tag for testing descriptions", body!.Description);
    }

    [Fact]
    public async Task CreateTag_WithoutDescription_ReturnsNullDescription()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "NoDescTag"
        }, CT);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Null(body!.Description);
    }

    [Fact]
    public async Task GetTags_IncludesDescription()
    {
        var client = await CreateAdminClientAsync();

        await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "DescGetTag",
            description = "visible in list"
        }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?Search=DescGetTag&IncludeChildren=false&Page=1&PageSize=50", CT);
        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        var tag = tags!.Items.Single(t => t.Name == "DescGetTag");
        Assert.Equal("visible in list", tag.Description);
    }

    [Fact]
    public async Task PatchTag_UpdatesDescription()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "PatchDescTag",
            description = "original"
        }, CT);
        createResp.EnsureSuccessStatusCode();
        var tag = await createResp.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{tag!.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object)"updated description" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("updated description", body!.Description);
    }

    [Fact]
    public async Task PatchTag_ClearDescription_SetsNull()
    {
        var client = await CreateAdminClientAsync();

        var createResp = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "ClearDescTag",
            description = "will be cleared"
        }, CT);
        createResp.EnsureSuccessStatusCode();
        var tag = await createResp.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{tag!.Id}", new[]
        {
            new { op = "replace", path = "/description", value = (object?)null! }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Null(body!.Description);

        // Verify persisted
        var getResp = await client.GetAsync("/api/v1/documents/tags?Search=ClearDescTag&IncludeChildren=false&Page=1&PageSize=50", CT);
        var tags = await getResp.Content.ReadFromJsonAsync<PaginatedResult<TagResult>>(CT);
        Assert.Null(tags!.Items.Single(t => t.Name == "ClearDescTag").Description);
    }

    private async Task AssignTagAsync(HttpClient client, Guid docId, string tagPath)
    {
        // Get current tag paths on the document
        var getResponse = await client.GetAsync($"/api/v1/documents/{docId}", CT);
        getResponse.EnsureSuccessStatusCode();
        var doc = await getResponse.Content.ReadFromJsonAsync<DocumentDetail>(CT);
        var currentPaths = doc!.Tags.Select(t => t.Path).ToList();
        currentPaths.Add(tagPath);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)currentPaths.Distinct().ToArray() }
        });
        patchResponse.EnsureSuccessStatusCode();
    }

    private record TagResult(Guid Id, string Name, string Path, Guid? ParentId, string? Color, string? EffectiveColor, string? Description,
        DateTimeOffset? UpdatedAt = null, Guid? UpdatedByUserId = null, string? UpdatedByUserName = null);
    private record TagListItem(Guid Id, string Name, string Path, string? Description, int DocumentCount,
        DateTimeOffset? UpdatedAt = null, Guid? UpdatedByUserId = null, string? UpdatedByUserName = null);
    private record DocumentDetail(Guid Id, string Name, IReadOnlyList<DocumentDetailTag> Tags);
    private record DocumentDetailTag(Guid Id, string Name, string Path);
}
