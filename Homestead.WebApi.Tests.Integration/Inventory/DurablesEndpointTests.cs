using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

[Collection("Integration")]
public class DurablesEndpointTests : IntegrationTestBase
{
    public DurablesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static Task<DurableItem> CreateContainerAsync(
        HttpClient client, string name, Guid? areaId = null, Guid? parentItemId = null) =>
        CreateDurableAsync(client, new { name, areaId, parentItemId, isContainer = true });

    private static async Task<DurableItem> GetDurableAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/durables/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var area = await resp.Content.ReadFromJsonAsync<AreaItem>(CT);
        return area!.Id;
    }

    [Fact]
    public async Task CreateDurable_AppliesNonFungibleActiveDefaults()
    {
        var client = await CreateAdminClientAsync();

        var item = await CreateDurableAsync(client, new { name = "Cordless drill" });

        Assert.Equal("Cordless drill", item.Name);
        Assert.Equal("NonFungible", item.Fungibility);
        Assert.Equal("Active", item.Status);
        Assert.False(item.IsContainer);
        Assert.Null(item.AreaId);
    }

    [Fact]
    public async Task PatchItem_MoveAndUnplace_UpdatesAreaId()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var item = await CreateDurableAsync(client, new { name = "Drill" });

        // Move into an area.
        var moveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/areaId", value = (object)garageId } });
        Assert.Equal(HttpStatusCode.OK, moveResp.StatusCode);
        var moved = await moveResp.Content.ReadFromJsonAsync<DurableItem>(CT);
        Assert.Equal(garageId, moved!.AreaId);
        Assert.Equal("Garage", moved.AreaName);

        // Unplace.
        var unplaceResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/areaId", value = (object?)null } });
        Assert.Equal(HttpStatusCode.OK, unplaceResp.StatusCode);
        var unplaced = await unplaceResp.Content.ReadFromJsonAsync<DurableItem>(CT);
        Assert.Null(unplaced!.AreaId);
        Assert.Null(unplaced.AreaName);
    }

    [Fact]
    public async Task PatchItem_LentRoundTrip_SetsThenAutoClearsLentTo()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Ladder" });

        var lendResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new object[]
            {
                new { op = "replace", path = "/status", value = (object)"Lent" },
                new { op = "replace", path = "/lentTo", value = (object)"Dave next door" },
            });
        Assert.Equal(HttpStatusCode.OK, lendResp.StatusCode);
        var lent = await lendResp.Content.ReadFromJsonAsync<DurableItem>(CT);
        Assert.Equal("Lent", lent!.Status);
        Assert.Equal("Dave next door", lent.LentTo);

        // Leaving Lent auto-clears the LentTo note.
        var returnResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Active" } });
        Assert.Equal(HttpStatusCode.OK, returnResp.StatusCode);
        var returned = await returnResp.Content.ReadFromJsonAsync<DurableItem>(CT);
        Assert.Equal("Active", returned!.Status);
        Assert.Null(returned.LentTo);
    }

    [Fact]
    public async Task PatchItem_LentWithoutLentTo_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Ladder" });

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Lent" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetDurables_ExcludesTerminalUnlessIncludeInactive()
    {
        var client = await CreateAdminClientAsync();
        var live = await CreateDurableAsync(client, new { name = "Live drill" });
        var archived = await CreateDurableAsync(client, new { name = "Old drill" });

        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{archived.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var defaultList = await (await client.GetAsync("/api/v1/inventory/durables?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);
        Assert.Contains(defaultList!.Items, i => i.Id == live.Id);
        Assert.DoesNotContain(defaultList.Items, i => i.Id == archived.Id);

        var inactiveList = await (await client.GetAsync(
                "/api/v1/inventory/durables?page=1&pageSize=200&includeInactive=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);
        Assert.Contains(inactiveList!.Items, i => i.Id == archived.Id);
    }

    // Regression guard for the reported 500: omitting page/pageSize must bind as optional and yield
    // the page defaults (1 / 50), not a binding failure. Pre-fix, [AsParameters] treated the
    // non-nullable int Page/PageSize as required and threw BadHttpRequestException → 500.
    [Fact]
    public async Task GetDurables_NoPaginationParams_Returns200WithDefaults()
    {
        var client = await CreateAdminClientAsync();
        await CreateDurableAsync(client, new { name = "Drill" });

        var resp = await client.GetAsync("/api/v1/inventory/durables", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);
        Assert.Equal(1, body!.Page);
        Assert.Equal(50, body.PageSize);
        Assert.Contains(body.Items, i => i.Name == "Drill");
    }

    // The exact report repro: a direct call to the API URL with ?lent=true and no pagination params.
    // `lent` is an unknown query param that [AsParameters] ignores; the 500 was purely the missing
    // page/pageSize. Must now return 200 with the defaults, independent of any lent data.
    [Fact]
    public async Task GetDurables_LentTrueWithoutPagination_Returns200WithDefaults()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync("/api/v1/inventory/durables?lent=true", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);
        Assert.Equal(1, body!.Page);
        Assert.Equal(50, body.PageSize);
    }

    // Hardening: a malformed (non-numeric) page must surface as 400, not 500. The binder throws
    // BadHttpRequestException, which DomainExceptionHandler now maps to its 400 status.
    [Fact]
    public async Task GetDurables_NonNumericPage_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync("/api/v1/inventory/durables?page=abc", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_CleanItem_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Disposable" });

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/durables/{item.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    // ----- Containment: happy paths -----

    [Fact]
    public async Task CreateDurable_InContainerWithCompartment_ResolvesPath()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);

        var wrench = await CreateDurableAsync(client,
            new { name = "Wrench", parentItemId = chest.Id, compartment = "Top drawer" });

        Assert.Equal(chest.Id, wrench.ParentItemId);
        Assert.Null(wrench.AreaId);
        Assert.Equal("Top drawer", wrench.Compartment);
        Assert.Equal(garageId, wrench.ResolvedAreaId);
        Assert.NotNull(wrench.Path);
        Assert.Equal(2, wrench.Path!.Count);
        Assert.Equal(garageId, wrench.Path[0].Id);
        Assert.Equal("Area", wrench.Path[0].Kind);
        Assert.Equal(chest.Id, wrench.Path[1].Id);
        Assert.Equal("Container", wrench.Path[1].Kind);
    }

    [Fact]
    public async Task PatchItem_MoveAreaToContainer_ViaOpPair()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: officeId);
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{drill.Id}",
            new object[]
            {
                new { op = "replace", path = "/areaId", value = (object?)null },
                new { op = "replace", path = "/parentItemId", value = (object)chest.Id },
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var moved = (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Null(moved.AreaId);
        Assert.Equal(chest.Id, moved.ParentItemId);
        Assert.Equal(officeId, moved.ResolvedAreaId);
    }

    [Fact]
    public async Task PatchItem_MoveContainer_ChildResolvedPathFollows()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        Assert.Equal(garageId, wrench.ResolvedAreaId);

        // Move only the container's own location; the child follows by derivation (one row written).
        var moveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/areaId", value = (object)officeId } });
        Assert.Equal(HttpStatusCode.OK, moveResp.StatusCode);

        var reloaded = await GetDurableAsync(client, wrench.Id);
        Assert.Equal(officeId, reloaded.ResolvedAreaId);
        Assert.Equal(chest.Id, reloaded.ParentItemId);
    }

    [Fact]
    public async Task GetDurables_ParentItemIdFilter_ReturnsDirectChildrenOnly()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var bin = await CreateContainerAsync(client, "Bin", parentItemId: chest.Id);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });
        var screw = await CreateDurableAsync(client, new { name = "Screw", parentItemId = bin.Id });

        var list = await (await client.GetAsync(
                $"/api/v1/inventory/durables?page=1&pageSize=200&parentItemId={chest.Id}", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);

        Assert.Contains(list!.Items, i => i.Id == bin.Id);
        Assert.Contains(list.Items, i => i.Id == wrench.Id);
        Assert.DoesNotContain(list.Items, i => i.Id == screw.Id);
    }

    [Fact]
    public async Task GetDurables_IsContainerFilter_ReturnsContainersOnly()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", areaId = garageId });

        var list = await (await client.GetAsync(
                "/api/v1/inventory/durables?page=1&pageSize=200&isContainer=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);

        Assert.Contains(list!.Items, i => i.Id == chest.Id);
        Assert.DoesNotContain(list.Items, i => i.Id == wrench.Id);
    }

    [Fact]
    public async Task GetDurable_NestedThreeDeep_ResolvesPathAndKinds()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var cupboard = await CreateContainerAsync(client, "Cupboard", areaId: garageId);
        var bin = await CreateContainerAsync(client, "Bin", parentItemId: cupboard.Id);
        var screw = await CreateDurableAsync(client, new { name = "Screw", parentItemId = bin.Id });

        var resp = await GetDurableAsync(client, screw.Id);

        Assert.Equal(garageId, resp.ResolvedAreaId);
        Assert.NotNull(resp.Path);
        Assert.Equal(3, resp.Path!.Count);
        Assert.Equal(garageId, resp.Path[0].Id);
        Assert.Equal("Area", resp.Path[0].Kind);
        Assert.Equal(cupboard.Id, resp.Path[1].Id);
        Assert.Equal("Container", resp.Path[1].Kind);
        Assert.Equal(bin.Id, resp.Path[2].Id);
        Assert.Equal("Container", resp.Path[2].Kind);
    }

    [Fact]
    public async Task GetDurable_UnplacedRootedChain_ResolvesToUnplaced()
    {
        var client = await CreateAdminClientAsync();
        var cupboard = await CreateContainerAsync(client, "Cupboard"); // unplaced
        var bin = await CreateContainerAsync(client, "Bin", parentItemId: cupboard.Id);
        var screw = await CreateDurableAsync(client, new { name = "Screw", parentItemId = bin.Id });

        var resp = await GetDurableAsync(client, screw.Id);

        Assert.Null(resp.ResolvedAreaId);
        Assert.NotNull(resp.Path);
        Assert.Equal(2, resp.Path!.Count);
        Assert.Equal("Container", resp.Path[0].Kind);
        Assert.Equal(cupboard.Id, resp.Path[0].Id);
        Assert.Equal(bin.Id, resp.Path[1].Id);
    }

    [Fact]
    public async Task GetDurable_ChainThroughTerminalContainer_StillResolves()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);

        // Seal the (empty) container, then place a live item inside it — the API permits placing
        // into a terminal container, and the resolver walks straight through it.
        var sealResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, sealResp.StatusCode);

        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        Assert.Equal(garageId, wrench.ResolvedAreaId);
        Assert.NotNull(wrench.Path);
        Assert.Equal(2, wrench.Path!.Count);
        Assert.Equal(garageId, wrench.Path[0].Id);
        Assert.Equal(chest.Id, wrench.Path[1].Id);
    }

    // ----- Containment: rejections (400) -----

    [Fact]
    public async Task CreateDurable_AreaAndParent_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", areaId = garageId, parentItemId = chest.Id }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDurable_ParentMissing_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", parentItemId = Guid.NewGuid() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDurable_ParentNotContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var plain = await CreateDurableAsync(client, new { name = "Hammer" });

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", parentItemId = plain.Id }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDurable_CompartmentWithoutParent_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", compartment = "Top drawer" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchItem_ParentWhileInArea_SingleOp_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });

        // Setting a parent without clearing the area leaves both location FKs set.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{drill.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object)chest.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchItem_SelfParent_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var chest = await CreateContainerAsync(client, "Tool chest");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object)chest.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchItem_CycleViaDescendant_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var a = await CreateContainerAsync(client, "A"); // unplaced
        var b = await CreateContainerAsync(client, "B", parentItemId: a.Id);
        var c = await CreateContainerAsync(client, "C", parentItemId: b.Id);

        // Placing A inside its own grandchild C forms a cycle.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{a.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object)c.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchItem_RoleOffWithChildren_Returns400_EvenAfterArchivingChild()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        var firstAttempt = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });
        Assert.Equal(HttpStatusCode.BadRequest, firstAttempt.StatusCode);

        // Archiving the child does not release the role-off block (Rule B: ANY child blocks).
        var archiveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, archiveResp.StatusCode);

        var retry = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });
        Assert.Equal(HttpStatusCode.BadRequest, retry.StatusCode);
    }

    [Fact]
    public async Task PatchItem_TerminalWithLiveChildren_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Sold" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchItem_TerminalWithOnlyTerminalChildren_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var sellResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Sold" } });
        Assert.Equal(HttpStatusCode.OK, sellResp.StatusCode);
        var sold = (await sellResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Equal("Sold", sold.Status);
        Assert.True(sold.IsContainer); // role stays on, preserving the invariant for leftovers.
    }

    // ----- Containment: escape hatch -----

    [Fact]
    public async Task PatchItem_TransitionToTerminal_IgnoresLocationOps()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });

        // Sell-and-move in one patch: the move ops are ignored for the live→terminal transition.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{drill.Id}",
            new object[]
            {
                new { op = "replace", path = "/status", value = (object)"Sold" },
                new { op = "replace", path = "/areaId", value = (object?)null },
                new { op = "replace", path = "/parentItemId", value = (object)chest.Id },
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sold = (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Equal("Sold", sold.Status);
        Assert.Equal(garageId, sold.AreaId);
        Assert.Null(sold.ParentItemId);
    }

    [Fact]
    public async Task PatchItem_AlreadyTerminal_MoveOutThenRoleOff_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client,
            new { name = "Wrench", parentItemId = chest.Id, compartment = "Top drawer" });

        // Make the child terminal (it stays inside the container).
        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        // Escape hatch: the already-terminal record can be moved out; compartment auto-clears.
        var moveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object?)null } });
        Assert.Equal(HttpStatusCode.OK, moveResp.StatusCode);
        var moved = (await moveResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Null(moved.ParentItemId);
        Assert.Null(moved.Compartment);

        // With the container now empty, role-off succeeds.
        var roleOffResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });
        Assert.Equal(HttpStatusCode.OK, roleOffResp.StatusCode);
        var noLongerContainer = (await roleOffResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.False(noLongerContainer.IsContainer);
    }
}
