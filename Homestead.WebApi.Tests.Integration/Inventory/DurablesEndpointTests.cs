using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name, Guid? parentAreaId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name, parentAreaId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var area = await resp.Content.ReadFromJsonAsync<AreaItem>(CT);
        return area!.Id;
    }

    private async Task<Guid> GetCurrencyIdAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities
            .Where(c => c.Kind == CommodityKind.Currency)
            .Select(c => c.Id)
            .FirstAsync(CT);
    }

    [Fact]
    public async Task CreateDurable_WithAcquisitionCluster_RoundTrips()
    {
        var client = await CreateAdminClientAsync();
        var currencyId = await GetCurrencyIdAsync();

        var created = await CreateDurableAsync(client, new
        {
            name = "Cordless drill",
            acquisitionKind = "Purchased",
            acquisitionCost = 189m,
            acquisitionCostCurrencyId = currencyId,
            acquiredFrom = "Home Depot",
        });

        var fetched = await GetDurableAsync(client, created.Id);
        Assert.Equal("Purchased", fetched.AcquisitionKind);
        Assert.Equal(189m, fetched.AcquisitionCost);
        Assert.Equal(currencyId, fetched.AcquisitionCostCurrencyId);
        Assert.NotNull(fetched.AcquisitionCostCurrencySymbol);
        Assert.Equal("Home Depot", fetched.AcquiredFrom);
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

        // Nothing may be placed INTO a terminal container (B1). But a container can go terminal with a
        // leftover inside it: place a live item, take the item terminal first (so the container's
        // sealing gate sees only a terminal child), then seal the container. The resolver must still
        // walk straight through the now-terminal container to the area.
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });
        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        var sealResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, sealResp.StatusCode);

        var resolved = await GetDurableAsync(client, wrench.Id);

        Assert.Equal(garageId, resolved.ResolvedAreaId);
        Assert.NotNull(resolved.Path);
        Assert.Equal(2, resolved.Path!.Count);
        Assert.Equal(garageId, resolved.Path[0].Id);
        Assert.Equal(chest.Id, resolved.Path[1].Id);
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
    public async Task CreateDurable_ParentFungibleConsumable_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name = "Screws" }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var consumable = (await createResp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;

        // A parent must be a non-fungible item; a fungible consumable cannot hold children.
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", parentItemId = consumable.Id }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateDurable_ComponentUnderRegularItem_ResolvesAsComponent()
    {
        var client = await CreateAdminClientAsync();
        // A regular (non-container) item can be a parent: its child is a component, not stored-in stock.
        var pc = await CreateDurableAsync(client, new { name = "Gaming PC" });
        var gpu = await CreateDurableAsync(client, new { name = "GPU", parentItemId = pc.Id });

        // The component's parent is a non-container, so parentIsContainer is false ("Part of X").
        var fetchedGpu = await GetDurableAsync(client, gpu.Id);
        Assert.Equal(pc.Id, fetchedGpu.ParentItemId);
        Assert.False(fetchedGpu.ParentIsContainer);

        // Having a live component makes the parent an assembly (derived HasComponents).
        var fetchedPc = await GetDurableAsync(client, pc.Id);
        Assert.False(fetchedPc.IsContainer);
        Assert.True(fetchedPc.HasComponents);
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
    public async Task PatchItem_UncontainerWithChildren_Returns400_EvenAfterArchivingChild()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });

        var firstAttempt = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });
        Assert.Equal(HttpStatusCode.BadRequest, firstAttempt.StatusCode);

        // Archiving the child does not release the flip block (ANY child — live or terminal — blocks).
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
        Assert.True(sold.IsContainer); // stays a container, preserving the invariant for leftovers.
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

        // With the container now empty, flipping it to a regular item succeeds.
        var roleOffResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });
        Assert.Equal(HttpStatusCode.OK, roleOffResp.StatusCode);
        var noLongerContainer = (await roleOffResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.False(noLongerContainer.IsContainer);
    }

    // ----- B3: the durables endpoint is the non-fungible surface -----

    [Fact]
    public async Task GetDurable_OnFungibleConsumableId_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name = "Screws" }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var consumable = (await createResp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;

        // A fungible consumable id is "not found" on the durables surface — never a consumable shaped
        // as a durable (mirrors DeleteItem_OnFungibleConsumableId_Returns404NotRawError).
        var resp = await client.GetAsync($"/api/v1/inventory/durables/{consumable.Id}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- B2: undefined enum values are rejected, not persisted -----

    [Fact]
    public async Task PatchDurable_WithUndefinedStatus_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Drill" });

        // The generated client can't produce an integer status; a raw JSON-Patch can. The handler's
        // Enum.IsDefined guard must reject 99 before it persists and re-serializes as a JSON number.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = 99 } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- B1: nothing may be placed INTO a terminal container -----

    [Fact]
    public async Task CreateDurable_IntoTerminalContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var chest = await CreateContainerAsync(client, "Tool chest");
        // Archiving the empty container is allowed; parenting a new item into it is not.
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "Wrench", parentItemId = chest.Id }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchDurable_ReparentIntoTerminalContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var chest = await CreateContainerAsync(client, "Tool chest");
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        var wrench = await CreateDurableAsync(client, new { name = "Wrench" });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object)chest.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // Block-universally pin: even an already-terminal item (which normally may be freely re-located
    // via the escape hatch) cannot be re-parented INTO a terminal container.
    [Fact]
    public async Task PatchDurable_TerminalItemReparentIntoTerminalContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var chest = await CreateContainerAsync(client, "Tool chest");
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{chest.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var wrench = await CreateDurableAsync(client, new { name = "Wrench" });
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{wrench.Id}",
            new[] { new { op = "replace", path = "/parentItemId", value = (object)chest.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Resolved-area list filtering (?areaId / ?unplaced) -----

    private static async Task<PaginatedResult<DurableItem>?> ListDurablesAsync(HttpClient client, string query)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/durables?{query}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<PaginatedResult<DurableItem>>(CT);
    }

    [Fact]
    public async Task GetDurables_AreaFilter_MatchesResolvedNotDirect()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var wrench = await CreateDurableAsync(client, new { name = "Wrench", parentItemId = chest.Id });
        var stapler = await CreateDurableAsync(client, new { name = "Stapler", areaId = officeId });

        var list = await ListDurablesAsync(client, $"page=1&pageSize=200&areaId={garageId}");

        // The nested wrench resolves to the garage (not directly placed) and matches; the office
        // stapler does not.
        Assert.Contains(list!.Items, i => i.Id == wrench.Id);
        Assert.Contains(list.Items, i => i.Id == chest.Id);
        Assert.DoesNotContain(list.Items, i => i.Id == stapler.Id);
    }

    [Fact]
    public async Task GetDurables_AreaFilter_ExpandsToDescendantArea()
    {
        var client = await CreateAdminClientAsync();
        var houseId = await CreateAreaAsync(client, "House");
        var bedroomId = await CreateAreaAsync(client, "Bedroom", parentAreaId: houseId);
        var lamp = await CreateDurableAsync(client, new { name = "Lamp", areaId = bedroomId });

        var list = await ListDurablesAsync(client, $"page=1&pageSize=200&areaId={houseId}");

        // Filtering the parent area rolls in an item resolved to a descendant area.
        Assert.Contains(list!.Items, i => i.Id == lamp.Id);
    }

    [Fact]
    public async Task GetDurables_Unplaced_MatchesBrokenContainerChain()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var floatingBin = await CreateContainerAsync(client, "Floating bin"); // no area
        var screw = await CreateDurableAsync(client, new { name = "Screw", parentItemId = floatingBin.Id });
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });

        var list = await ListDurablesAsync(client, "page=1&pageSize=200&unplaced=true");

        Assert.Contains(list!.Items, i => i.Id == screw.Id);       // resolves to no area
        Assert.Contains(list.Items, i => i.Id == floatingBin.Id);
        Assert.DoesNotContain(list.Items, i => i.Id == drill.Id);
    }

    [Fact]
    public async Task GetDurables_AreaFilter_TotalCountReflectsFilteredSet()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        for (var i = 0; i < 3; i++)
            await CreateDurableAsync(client, new { name = $"Garage tool {i}", areaId = garageId });
        await CreateDurableAsync(client, new { name = "Office stapler", areaId = officeId });

        // pageSize below the filtered count so TotalCount can't be the page-local count.
        var list = await ListDurablesAsync(client, $"page=1&pageSize=2&areaId={garageId}");

        Assert.Equal(2, list!.Items.Count);
        Assert.Equal(3, list.TotalCount); // all garage items, not the page or the office one
    }

    // ----- Classification tags (create/patch whole-set-replace + TagIds facet) -----

    // Reads the global tag list. ItemTagRef binds the fields we assert on; the extra list columns
    // (ParentId, timestamps, …) are ignored by System.Text.Json.
    private static async Task<IReadOnlyList<ItemTagRef>> ListTagsAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/inventory/tags?Page=1&PageSize=200", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PaginatedResult<ItemTagRef>>(CT))!.Items;
    }

    [Fact]
    public async Task CreateDurable_WithTags_PopulatesResponseAndUpsertsAncestors()
    {
        var client = await CreateAdminClientAsync();

        var item = await CreateDurableAsync(client, new { name = "Drill", tags = new[] { "Tools/Cordless" } });

        // Response carries the leaf badge.
        var badge = Assert.Single(item.Tags!);
        Assert.Equal("Cordless", badge.Name);
        Assert.Equal("Tools/Cordless", badge.Path);

        // Both the leaf and its ancestor are upserted into the tag tree.
        var tags = await ListTagsAsync(client);
        Assert.Contains(tags, t => t.Path == "Tools");
        Assert.Contains(tags, t => t.Path == "Tools/Cordless");

        // The join is written — visible on the item detail read.
        var detail = await GetDurableAsync(client, item.Id);
        Assert.Single(detail.Tags!, t => t.Path == "Tools/Cordless");
    }

    [Fact]
    public async Task PatchDurable_ReplaceTags_WholeSetReplace()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Drill", tags = new[] { "Old" } });
        Assert.Single(item.Tags!, t => t.Name == "Old");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/tags", value = (object)new[] { "New" } } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Single(patched.Tags!, t => t.Name == "New");
        Assert.DoesNotContain(patched.Tags!, t => t.Name == "Old"); // whole set swapped
    }

    [Fact]
    public async Task PatchDurable_NoTagsOp_LeavesTagsUntouched()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Drill", tags = new[] { "Keep" } });

        // A patch with no /tags op is seeded from the current set, so tags survive untouched.
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Renamed drill" } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Equal("Renamed drill", patched.Name);
        Assert.Single(patched.Tags!, t => t.Name == "Keep");
    }

    [Fact]
    public async Task PatchDurable_ClearTags_EmptyArray()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateDurableAsync(client, new { name = "Drill", tags = new[] { "Remove" } });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/tags", value = (object)Array.Empty<string>() } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        Assert.Empty(patched.Tags!);
    }

    [Fact]
    public async Task GetDurables_TagIdsFilter_Narrows()
    {
        var client = await CreateAdminClientAsync();
        var tagged = await CreateDurableAsync(client, new { name = "Tagged drill", tags = new[] { "Facet" } });
        await CreateDurableAsync(client, new { name = "Plain drill" });
        var facetId = tagged.Tags!.Single().Id;

        var list = await ListDurablesAsync(client, $"page=1&pageSize=200&TagIds={facetId}");

        var only = Assert.Single(list!.Items);
        Assert.Equal(tagged.Id, only.Id);
    }

    [Fact]
    public async Task GetDurables_IncludeChildTags_PullsDescendants()
    {
        var client = await CreateAdminClientAsync();
        // The item carries only the leaf; filtering by the parent needs subtree expansion to match.
        var child = await CreateDurableAsync(client, new { name = "Child-tagged", tags = new[] { "Parent/Child" } });
        var parentId = (await ListTagsAsync(client)).Single(t => t.Path == "Parent").Id;

        // Without expansion the parent id matches nothing (the item holds only the descendant tag).
        var without = await ListDurablesAsync(client, $"page=1&pageSize=200&TagIds={parentId}");
        Assert.DoesNotContain(without!.Items, i => i.Id == child.Id);

        // IncludeChildTags expands the parent to its subtree, so the child-tagged item matches.
        var with = await ListDurablesAsync(client, $"page=1&pageSize=200&TagIds={parentId}&IncludeChildTags=true");
        Assert.Contains(with!.Items, i => i.Id == child.Id);
    }

    [Fact]
    public async Task GetDurables_TagFilter_ComposesWithAreaFilter()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        var inGarage = await CreateDurableAsync(client,
            new { name = "Garage tagged", areaId = garageId, tags = new[] { "Facet" } });
        var facetId = inGarage.Tags!.Single().Id;
        await CreateDurableAsync(client, new { name = "Garage plain", areaId = garageId });
        await CreateDurableAsync(client, new { name = "Office tagged", areaId = officeId, tags = new[] { "Facet" } });

        // Facet alone would match both tagged items; intersecting with the garage area narrows to one.
        var list = await ListDurablesAsync(client, $"page=1&pageSize=200&TagIds={facetId}&areaId={garageId}");

        var only = Assert.Single(list!.Items);
        Assert.Equal(inGarage.Id, only.Id);
    }
}
