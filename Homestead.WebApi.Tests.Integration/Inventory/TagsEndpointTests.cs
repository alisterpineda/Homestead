using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// Backend integration coverage for the household-global Inventory tag endpoints (/api/v1/inventory/tags).
// Mirrors the Documents tag suite but drops every ACL/visibility case — Inventory tags have no per-item
// access control. This is the first and only place the ILike Search branch, the unique-Path 409, the
// item_tags FK cascade, and the resolve/subtree SQL are exercised against a real Postgres.
[Collection("Integration")]
public class TagsEndpointTests : IntegrationTestBase
{
    public TagsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<InvTag> CreateTagAsync(
        HttpClient client, string path, string? color = null, string? description = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags",
            new { path, color, description }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<InvTag>(CT))!;
    }

    private static async Task<PaginatedResult<InvTag>> ListTagsAsync(HttpClient client, string query)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/tags?{query}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PaginatedResult<InvTag>>(CT))!;
    }

    // Tags earn an ItemCount / a surviving-join assertion only by riding on a real item, so seed them
    // through the durable-create tags[] path (the same whole-set-replace the item suites exercise).
    private static async Task<TaggedDurable> CreateDurableWithTagsAsync(
        HttpClient client, string name, params string[] tags)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name, tags }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<TaggedDurable>(CT))!;
    }

    private static async Task<ItemDetailWithTags> GetItemAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/items/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemDetailWithTags>(CT))!;
    }

    // ----- CRUD + path -----

    [Fact]
    public async Task CreateTag_ReturnsCreated_LeafColorMaterialised()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags",
            new { path = "Electronics", color = "#3b82f6" }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<InvTag>(CT))!;
        Assert.Equal("Electronics", body.Name);
        Assert.Equal("Electronics", body.Path);
        Assert.Null(body.ParentId);
        Assert.Equal("#3b82f6", body.Color);
        // A leaf with an explicit colour materialises EffectiveColor == Color.
        Assert.Equal("#3b82f6", body.EffectiveColor);
        // Location header points at the created resource.
        Assert.NotNull(resp.Headers.Location);
        Assert.Contains(body.Id.ToString(), resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreateTag_NestedPath_UpsertsAncestors()
    {
        var client = await CreateAdminClientAsync();

        var leaf = await CreateTagAsync(client, "Electronics/Batteries/AA",
            color: "#abcdef", description: "AA cells");

        var list = await ListTagsAsync(client, "Page=1&PageSize=50&SortBy=Path");
        Assert.Equal(3, list.TotalCount);
        var electronics = list.Items.Single(t => t.Path == "Electronics");
        var batteries = list.Items.Single(t => t.Path == "Electronics/Batteries");
        var aa = list.Items.Single(t => t.Path == "Electronics/Batteries/AA");

        // Chained ParentId root→leaf.
        Assert.Null(electronics.ParentId);
        Assert.Equal(electronics.Id, batteries.ParentId);
        Assert.Equal(batteries.Id, aa.ParentId);
        Assert.Equal(leaf.Id, aa.Id);

        // Only the leaf carries the supplied colour/description; upserted ancestors get neither.
        Assert.Null(electronics.Color);
        Assert.Null(electronics.Description);
        Assert.Null(batteries.Color);
        Assert.Null(batteries.Description);
        Assert.Equal("#abcdef", aa.Color);
        Assert.Equal("AA cells", aa.Description);
    }

    [Fact]
    public async Task CreateTag_EmptyPath_Returns400()
    {
        var client = await CreateAdminClientAsync();

        // "/" normalises to zero segments — a reachable bad-request, not a 500.
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags", new { path = "/" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateTag_ExistingPath_Idempotent_UpdatesColorDescriptionOnly()
    {
        var client = await CreateAdminClientAsync();

        var first = await CreateTagAsync(client, "Electronics", color: "#111111", description: "first");

        // Re-POST the same path with a new colour + description: same row, values updated.
        var updated = await CreateTagAsync(client, "Electronics", color: "#222222", description: "second");
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal("#222222", updated.Color);
        Assert.Equal("second", updated.Description);

        // A second re-POST omitting colour/description leaves both unchanged (null ⇒ no change).
        var unchanged = await CreateTagAsync(client, "Electronics");
        Assert.Equal(first.Id, unchanged.Id);
        Assert.Equal("#222222", unchanged.Color);
        Assert.Equal("second", unchanged.Description);

        // No duplicate row was created across the three POSTs — the "Electronics" search returns
        // exactly one row (a predicate on the PK id would pass vacuously, since a dup carries a new id).
        var list = await ListTagsAsync(client, "Search=Electronics&Page=1&PageSize=50");
        var only = Assert.Single(list.Items);
        Assert.Equal(first.Id, only.Id);
    }

    [Fact]
    public async Task CreateTag_ChildInheritsParentEffectiveColor()
    {
        var client = await CreateAdminClientAsync();

        await CreateTagAsync(client, "Electronics", color: "#3b82f6");
        var child = await CreateTagAsync(client, "Electronics/Batteries");

        Assert.Null(child.Color);
        Assert.Equal("#3b82f6", child.EffectiveColor);
    }

    // ----- GET list / search / sort / parent-path -----

    [Fact]
    public async Task GetTags_Paginates()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 0; i < 5; i++)
            await CreateTagAsync(client, $"Tag{i}");

        var page = await ListTagsAsync(client, "Page=1&PageSize=2&SortBy=Name&SortDir=Asc");

        Assert.Equal(5, page.TotalCount); // whole set, not the page
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetTags_Search_ILike_CaseInsensitive()
    {
        var client = await CreateAdminClientAsync();
        // "AA" carries no "batt" in its NAME but does in its PATH — the InMemory unit layer can't
        // translate ILike, so this branch is proven only here.
        await CreateTagAsync(client, "Batteries/AA");

        var list = await ListTagsAsync(client, "Search=batt&Page=1&PageSize=50");

        // Lower-case "batt" matches capitalised "Batteries" ⇒ case-insensitive name branch.
        Assert.Contains(list.Items, t => t.Name == "Batteries");
        // "AA" matches only because its path contains "Batteries" ⇒ the path branch.
        Assert.Contains(list.Items, t => t.Name == "AA");
    }

    [Fact]
    public async Task GetTags_ParentPath_DirectChildrenOnly()
    {
        var client = await CreateAdminClientAsync();
        await CreateTagAsync(client, "Electronics/Batteries/AA");
        await CreateTagAsync(client, "Electronics/Cables");

        var list = await ListTagsAsync(client, "ParentPath=Electronics&IncludeChildren=false&Page=1&PageSize=50");

        Assert.Contains(list.Items, t => t.Path == "Electronics/Batteries");
        Assert.Contains(list.Items, t => t.Path == "Electronics/Cables");
        Assert.DoesNotContain(list.Items, t => t.Path == "Electronics/Batteries/AA"); // grandchild excluded
        Assert.DoesNotContain(list.Items, t => t.Path == "Electronics");               // parent itself excluded
    }

    [Fact]
    public async Task GetTags_ParentPath_IncludeChildren_ReturnsSubtree()
    {
        var client = await CreateAdminClientAsync();
        await CreateTagAsync(client, "Electronics/Batteries/AA");
        await CreateTagAsync(client, "Electronics/Cables");

        var list = await ListTagsAsync(client, "ParentPath=Electronics&IncludeChildren=true&Page=1&PageSize=50");

        Assert.Contains(list.Items, t => t.Path == "Electronics/Batteries");
        Assert.Contains(list.Items, t => t.Path == "Electronics/Batteries/AA"); // whole subtree
        Assert.Contains(list.Items, t => t.Path == "Electronics/Cables");
        Assert.DoesNotContain(list.Items, t => t.Path == "Electronics");         // subtree excludes the root itself
    }

    [Fact]
    public async Task GetTags_SortByItemCountDesc()
    {
        var client = await CreateAdminClientAsync();
        await CreateDurableWithTagsAsync(client, "Drill", "Popular");
        await CreateDurableWithTagsAsync(client, "Saw", "Popular");   // Popular ⇒ 2 items
        await CreateDurableWithTagsAsync(client, "Wrench", "Medium");  // Medium  ⇒ 1 item
        await CreateTagAsync(client, "Zero");                          // Zero    ⇒ 0 items

        var list = await ListTagsAsync(client, "Page=1&PageSize=50&SortBy=ItemCount&SortDir=Desc");

        var names = list.Items.Select(t => t.Name).ToList();
        Assert.Equal("Popular", names[0]);   // most-tagged first
        Assert.Equal("Zero", names[^1]);      // untagged last
        Assert.Equal(2, list.Items.Single(t => t.Name == "Popular").ItemCount);
        Assert.Equal(1, list.Items.Single(t => t.Name == "Medium").ItemCount);
        Assert.Equal(0, list.Items.Single(t => t.Name == "Zero").ItemCount);
    }

    [Fact]
    public async Task GetTags_DefaultSort_IsNameAsc()
    {
        var client = await CreateAdminClientAsync();
        // Insertion order deliberately differs from name order.
        await CreateTagAsync(client, "Zulu");
        await CreateTagAsync(client, "Alpha");
        await CreateTagAsync(client, "Mike");

        var byDefault = await ListTagsAsync(client, "Page=1&PageSize=50");
        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, byDefault.Items.Select(t => t.Name).ToArray());

        // Path sort translates too (cheap extra assertion over the same set).
        var byPath = await ListTagsAsync(client, "Page=1&PageSize=50&SortBy=Path&SortDir=Asc");
        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, byPath.Items.Select(t => t.Name).ToArray());
    }

    // ----- PATCH -----

    [Fact]
    public async Task PatchTag_Rename_ReDerivesDescendantPaths()
    {
        var client = await CreateAdminClientAsync();
        var electronics = await CreateTagAsync(client, "Electronics");
        await CreateTagAsync(client, "Electronics/Batteries");
        await CreateTagAsync(client, "Electronics/Batteries/AA");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{electronics.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Gadgets" } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var list = await ListTagsAsync(client, "Page=1&PageSize=50");
        Assert.Equal("Gadgets", list.Items.Single(t => t.Id == electronics.Id).Path);
        Assert.Equal("Gadgets/Batteries", list.Items.Single(t => t.Name == "Batteries").Path);
        Assert.Equal("Gadgets/Batteries/AA", list.Items.Single(t => t.Name == "AA").Path);
    }

    [Fact]
    public async Task PatchTag_Reparent_UpdatesPathAndEffectiveColor()
    {
        var client = await CreateAdminClientAsync();
        await CreateTagAsync(client, "Blue", color: "#3b82f6");
        var red = await CreateTagAsync(client, "Red", color: "#ef4444");
        var movable = await CreateTagAsync(client, "Blue/Movable");
        Assert.Equal("#3b82f6", movable.EffectiveColor);

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{movable.Id}",
            new[] { new { op = "replace", path = "/parentId", value = (object)red.Id.ToString() } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var updated = (await resp.Content.ReadFromJsonAsync<InvTag>(CT))!;
        Assert.Equal(red.Id, updated.ParentId);
        Assert.Equal("Red/Movable", updated.Path);
        Assert.Equal("#ef4444", updated.EffectiveColor); // inherits the new parent's colour
    }

    [Fact]
    public async Task PatchTag_Recolour_CascadesToDescendants()
    {
        var client = await CreateAdminClientAsync();
        var parent = await CreateTagAsync(client, "Parent");
        await CreateTagAsync(client, "Parent/Child");

        await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{parent.Id}",
            new[] { new { op = "replace", path = "/color", value = (object)"#ef4444" } });

        var list = await ListTagsAsync(client, "Search=Parent&Page=1&PageSize=50");
        Assert.Equal("#ef4444", list.Items.Single(t => t.Name == "Child").EffectiveColor);
    }

    [Fact]
    public async Task PatchTag_ClearColour_InheritsGrandparent()
    {
        var client = await CreateAdminClientAsync();
        await CreateTagAsync(client, "Gp", color: "#3b82f6");
        var mid = await CreateTagAsync(client, "Gp/Mid", color: "#ef4444");
        await CreateTagAsync(client, "Gp/Mid/Leaf");

        // Clearing Mid's colour re-roots the leaf's inheritance at the grandparent.
        await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{mid.Id}",
            new[] { new { op = "replace", path = "/color", value = (object?)null! } });

        var list = await ListTagsAsync(client, "Search=Gp&Page=1&PageSize=50");
        Assert.Equal("#3b82f6", list.Items.Single(t => t.Name == "Mid").EffectiveColor);
        Assert.Equal("#3b82f6", list.Items.Single(t => t.Name == "Leaf").EffectiveColor);
    }

    [Fact]
    public async Task PatchTag_RenameCollision_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateTagAsync(client, "Alpha");
        var beta = await CreateTagAsync(client, "Beta");

        // Renaming Beta onto the existing "Alpha" path trips the unique Path index → 409.
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{beta.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Alpha" } });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PatchTag_CircularParent_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var parent = await CreateTagAsync(client, "Parent");
        var child = await CreateTagAsync(client, "Parent/Child");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{parent.Id}",
            new[] { new { op = "replace", path = "/parentId", value = (object)child.Id.ToString() } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchTag_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{Guid.NewGuid()}",
            new[] { new { op = "replace", path = "/name", value = (object)"Whatever" } });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- DELETE / bulk-delete / re-parent (join semantics) -----

    [Fact]
    public async Task DeleteTag_Returns204_ThenGone()
    {
        var client = await CreateAdminClientAsync();
        var tag = await CreateTagAsync(client, "Deletable");

        var resp = await client.DeleteAsync($"/api/v1/inventory/tags/{tag.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var list = await ListTagsAsync(client, "Page=1&PageSize=50");
        Assert.DoesNotContain(list.Items, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task DeleteTag_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.DeleteAsync($"/api/v1/inventory/tags/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_ReturnsDeletedCount()
    {
        var client = await CreateAdminClientAsync();
        var a = await CreateTagAsync(client, "BulkA");
        var b = await CreateTagAsync(client, "BulkB");

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/bulk-delete",
            new { ids = new[] { a.Id, b.Id } }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    [Fact]
    public async Task BulkDelete_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/bulk-delete",
            new { ids = Array.Empty<Guid>() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteParentTag_ReparentsChild_ItemJoinSurvives()
    {
        var client = await CreateAdminClientAsync();
        // Tag an item with the leaf "Electronics/Batteries" — the join points at Batteries.
        var durable = await CreateDurableWithTagsAsync(client, "Cordless drill", "Electronics/Batteries");
        var badge = Assert.Single(durable.Tags!);
        Assert.Equal("Batteries", badge.Name);
        Assert.Equal("Electronics/Batteries", badge.Path);
        var batteriesId = badge.Id;

        var before = await ListTagsAsync(client, "Page=1&PageSize=50");
        var electronics = before.Items.Single(t => t.Path == "Electronics");

        // Delete the root Electronics — no in-use block; Batteries is re-parented to root.
        var del = await client.DeleteAsync($"/api/v1/inventory/tags/{electronics.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var after = await ListTagsAsync(client, "Page=1&PageSize=50");
        Assert.DoesNotContain(after.Items, t => t.Id == electronics.Id);
        var batteries = after.Items.Single(t => t.Id == batteriesId);
        Assert.Null(batteries.ParentId);
        Assert.Equal("Batteries", batteries.Path); // now a root

        // The item's join survived: same tag id, now surfaced as the re-parented "Batteries". The item
        // row itself is untouched.
        var detail = await GetItemAsync(client, durable.Id);
        Assert.NotNull(detail.Durable);
        var stillTagged = Assert.Single(detail.Durable!.Tags);
        Assert.Equal(batteriesId, stillTagged.Id);
        Assert.Equal("Batteries", stillTagged.Name);
        Assert.Equal("Batteries", stillTagged.Path);
        Assert.Equal("Cordless drill", detail.Durable.Name);
    }

    [Fact]
    public async Task DeleteTaggedLeaf_UntagsItem_ItemSurvives()
    {
        var client = await CreateAdminClientAsync();
        var durable = await CreateDurableWithTagsAsync(client, "Cordless drill", "Tools/Hammer");
        var badge = Assert.Single(durable.Tags!);
        Assert.Equal("Hammer", badge.Name);

        // Delete the exact leaf the item carries — the join cascade-removes; the item stays.
        var del = await client.DeleteAsync($"/api/v1/inventory/tags/{badge.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var detail = await GetItemAsync(client, durable.Id);
        Assert.NotNull(detail.Durable);
        Assert.Empty(detail.Durable!.Tags);              // untagged (join gone)
        Assert.Equal("Cordless drill", detail.Durable.Name); // item survives
    }

    [Fact]
    public async Task BulkDelete_ParentAndChild_DeepestFirst()
    {
        var client = await CreateAdminClientAsync();
        var electronics = await CreateTagAsync(client, "Electronics");
        var batteries = await CreateTagAsync(client, "Electronics/Batteries");
        var aa = await CreateTagAsync(client, "Electronics/Batteries/AA");

        // Delete parent + middle together; deepest-first processing re-parents the grandchild AA to a
        // deterministic root.
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/bulk-delete",
            new { ids = new[] { electronics.Id, batteries.Id } }, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);

        var list = await ListTagsAsync(client, "Page=1&PageSize=50");
        var survivingAa = list.Items.Single(t => t.Id == aa.Id);
        Assert.Null(survivingAa.ParentId);
        Assert.Equal("AA", survivingAa.Path); // re-parented to root
        Assert.DoesNotContain(list.Items, t => t.Id == electronics.Id);
        Assert.DoesNotContain(list.Items, t => t.Id == batteries.Id);
    }

    // ----- Resolve -----

    [Fact]
    public async Task ResolveTags_KnownIds_ReturnsIdAndPath()
    {
        var client = await CreateAdminClientAsync();
        var a = await CreateTagAsync(client, "ResolveA");
        var b = await CreateTagAsync(client, "Parent/ResolveB");

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/resolve",
            new { ids = new[] { a.Id, b.Id } }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<InvTagResolve>>(CT);
        Assert.Equal(2, body!.Count);
        Assert.Contains(body, x => x.Id == a.Id && x.Path == "ResolveA");
        Assert.Contains(body, x => x.Id == b.Id && x.Path == "Parent/ResolveB");
    }

    [Fact]
    public async Task ResolveTags_UnknownIds_SilentlyDropped()
    {
        var client = await CreateAdminClientAsync();
        var a = await CreateTagAsync(client, "ResolveA");

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/resolve",
            new { ids = new[] { a.Id, Guid.NewGuid() } }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<InvTagResolve>>(CT);
        Assert.NotNull(body);
        Assert.Single(body);
        Assert.Equal(a.Id, body[0].Id);
    }

    [Fact]
    public async Task ResolveTags_EmptyIds_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/resolve",
            new { ids = Array.Empty<Guid>() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ResolveTags_Over200Ids_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags/resolve", new { ids }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Auth matrix (Contributor gate on writes; ActiveUser admits Viewer on reads) -----

    [Fact]
    public async Task CreateTag_AsViewer_Returns403()
    {
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync("/api/v1/inventory/tags", new { path = "Nope" }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PatchTag_AsViewer_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var tag = await CreateTagAsync(admin, "Electronics");
        var viewer = await CreateViewerClientAsync();

        var resp = await SendJsonPatchAsync(viewer, $"/api/v1/inventory/tags/{tag.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Renamed" } });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteTag_AsViewer_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var tag = await CreateTagAsync(admin, "Electronics");
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.DeleteAsync($"/api/v1/inventory/tags/{tag.Id}", CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_AsViewer_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var tag = await CreateTagAsync(admin, "Electronics");
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync("/api/v1/inventory/tags/bulk-delete",
            new { ids = new[] { tag.Id } }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateTag_AsInactiveContributor_Returns403()
    {
        var client = await CreateUserClientAsync(isActive: false);

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags", new { path = "Nope" }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetTags_AsViewer_Returns200()
    {
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.GetAsync("/api/v1/inventory/tags?Page=1&PageSize=50", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ResolveTags_AsViewer_Returns200()
    {
        var admin = await CreateAdminClientAsync();
        var tag = await CreateTagAsync(admin, "Electronics");
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync("/api/v1/inventory/tags/resolve",
            new { ids = new[] { tag.Id } }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<List<InvTagResolve>>(CT);
        Assert.Single(body!);
    }

    [Fact]
    public async Task GetTags_AsInactive_Returns403()
    {
        var client = await CreateUserClientAsync(isActive: false);

        var resp = await client.GetAsync("/api/v1/inventory/tags?Page=1&PageSize=50", CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateTag_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/tags", new { path = "Nope" }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ----- EffectiveColor persistence -----

    [Fact]
    public async Task EffectiveColor_IsMaterialised_ReadViaGet()
    {
        var client = await CreateAdminClientAsync();
        var parent = await CreateTagAsync(client, "Parent");
        await CreateTagAsync(client, "Parent/Child");

        // Recolour the parent, then read the descendant back: EffectiveColor comes off the materialised
        // column, not a read-time recompute.
        await SendJsonPatchAsync(client, $"/api/v1/inventory/tags/{parent.Id}",
            new[] { new { op = "replace", path = "/color", value = (object)"#ef4444" } });

        var list = await ListTagsAsync(client, "Search=Parent&Page=1&PageSize=50");
        var child = list.Items.Single(t => t.Name == "Child");
        Assert.Null(child.Color);
        Assert.Equal("#ef4444", child.EffectiveColor);
    }

    // ----- Local DTO records (enum fields as string; reuse PaginatedResult<T>/BulkDeleteResult) -----

    // One record binds the create, patch, and list shapes — trailing params default, so the create
    // response (which omits ItemCount/UpdatedBy*/timestamps) and the patch response still deserialize.
    private record InvTag(Guid Id, string Name, string Path, Guid? ParentId,
        string? Color, string? EffectiveColor, string? Description,
        DateTimeOffset CreatedAt = default, DateTimeOffset UpdatedAt = default,
        Guid? UpdatedByUserId = null, string? UpdatedByUserName = null, int ItemCount = 0);

    private record InvTagResolve(Guid Id, string Path);

    // Minimal projections of the durable-create response and the unified item detail — System.Text.Json
    // binds by name and ignores the fields we don't declare. Badge shape reuses the namespace-shared
    // ItemTagRef (AreasEndpointTests.cs).
    private record TaggedDurable(Guid Id, string Name, IReadOnlyList<ItemTagRef> Tags);
    private record ItemDetailWithTags(Guid Id, string Fungibility, TaggedDurable? Durable);
}
