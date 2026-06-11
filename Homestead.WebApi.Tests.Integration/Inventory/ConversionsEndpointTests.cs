using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// The track-one response shape: refreshed supply detail + the freshly-created individual item.
internal record TrackItemResultItem(SupplyDetailItem Detail, ItemItem Item);

[Collection("Integration")]
public class ConversionsEndpointTests : IntegrationTestBase
{
    public ConversionsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<SupplyItem> CreateSupplyAsync(
        HttpClient client, string name = "Screws", string unit = "screws")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/supplies", new { name, unit }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SupplyItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static async Task<ItemItem> CreateContainerAsync(HttpClient client, string name, Guid? areaId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items",
            new { name, areaId, isContainer = true }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    private static async Task<ItemItem> CreatePlainItemAsync(
        HttpClient client, string name, Guid? areaId = null, Guid? parentItemId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items",
            new { name, areaId, parentItemId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/receive", body, CT);

    private static Task<HttpResponseMessage> TrackOneAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/track-one", body, CT);

    private static Task<HttpResponseMessage> CollapseAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/collapse", body, CT);

    private static Task<HttpResponseMessage> ConsumeAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/consume", body, CT);

    private static async Task<TrackItemResultItem> OkTrackAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<TrackItemResultItem>(CT))!;
    }

    private static async Task<SupplyDetailItem> OkDetailAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SupplyDetailItem>(CT))!;
    }

    private static async Task<SupplyDetailItem> GetSupplyAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/supplies/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SupplyDetailItem>(CT))!;
    }

    private static async Task<ItemItem> GetItemAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/items/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    private static async Task<IReadOnlyList<StockMovementItem>> GetMovementsAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/supplies/{id}/movements?page=1&pageSize=100", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PaginatedResult<StockMovementItem>>(CT);
        return page!.Items;
    }

    private static async Task ArchiveSupplyAsync(HttpClient client, Guid supplyId)
    {
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/supplies/{supplyId}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ----- Track one individually -----

    [Fact]
    public async Task TrackOne_DecrementsCreatesItemAndConversionOutflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client, "Drill bits", "bits");
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var result = await OkTrackAsync(await TrackOneAsync(client, supply.Id,
            new { areaId, name = "Drill bit #1" }));

        // Refreshed detail: stock dropped by exactly one.
        Assert.Equal(4m, result.Detail.Supply.TotalOnHand);
        var entry = Assert.Single(result.Detail.Locations);
        Assert.Equal(4m, entry.Quantity);

        // The new individual item.
        Assert.Equal("Drill bit #1", result.Item.Name);
        Assert.Equal("NonFungible", result.Item.Fungibility);
        Assert.Equal("Active", result.Item.Status);
        Assert.Equal(areaId, result.Item.AreaId);
        Assert.Null(result.Item.ParentItemId);
        Assert.False(result.Item.IsContainer);

        // The conversion outflow carries the new item's id for two-way traceability.
        var conversions = (await GetMovementsAsync(client, supply.Id)).Where(m => m.Kind == "Conversion").ToList();
        var outflow = Assert.Single(conversions);
        Assert.Equal(1m, outflow.Quantity);
        Assert.Equal(areaId, outflow.FromAreaId);
        Assert.Null(outflow.ToAreaId);
        Assert.Equal(result.Item.Id, outflow.RelatedItemId);
        Assert.Equal("Drill bit #1", outflow.RelatedItemName);
    }

    [Fact]
    public async Task TrackOne_FromContainer_PlacesItemInContainerWithCompartment()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { containerItemId = bin.Id, quantity = 3m });

        var result = await OkTrackAsync(await TrackOneAsync(client, supply.Id,
            new { containerItemId = bin.Id, name = "Special screw", compartment = "Top tray" }));

        Assert.Equal(bin.Id, result.Item.ParentItemId);
        Assert.Null(result.Item.AreaId);
        Assert.Equal("Top tray", result.Item.Compartment);
        Assert.Equal(2m, result.Detail.Supply.TotalOnHand);
    }

    [Fact]
    public async Task TrackOne_LastUnit_RemovesEntry()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 1m });

        var result = await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Last one" }));

        // A zero-quantity entry is purged, not kept at zero.
        Assert.Empty(result.Detail.Locations);
        Assert.Equal(0m, result.Detail.Supply.TotalOnHand);
    }

    [Fact]
    public async Task TrackOne_FractionalQuantity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client, "Rope", "m");
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 3.5m });

        var resp = await TrackOneAsync(client, supply.Id, new { areaId, name = "A length" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_QuantityBelowOne_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client, "Rope", "m");
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 0.5m });

        var resp = await TrackOneAsync(client, supply.Id, new { areaId, name = "A length" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_NoStockAtLocation_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var resp = await TrackOneAsync(client, supply.Id, new { areaId, name = "Phantom" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_ArchivedSupply_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        await ArchiveSupplyAsync(client, supply.Id);

        var resp = await TrackOneAsync(client, supply.Id, new { areaId, name = "Nope" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_OnNonFungibleItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var item = await CreatePlainItemAsync(client, "Hammer", areaId: areaId);

        var resp = await TrackOneAsync(client, item.Id, new { areaId, name = "From a non-supply" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_MissingName_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await TrackOneAsync(client, supply.Id, new { areaId, name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_BothLocations_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: areaId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await TrackOneAsync(client, supply.Id,
            new { areaId, containerItemId = bin.Id, name = "Ambiguous" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Collapse back to a count -----

    [Fact]
    public async Task Collapse_TwoItems_AddsTwoWithDistinctInflowsAndArchivesItems()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var a = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Screw A" }))).Item;
        var b = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Screw B" }))).Item;
        // Stock now 3 at the area; two individual items carved out.

        var detail = await OkDetailAsync(await CollapseAsync(client, supply.Id,
            new { itemIds = new[] { a.Id, b.Id }, areaId }));

        // +2 merged back into the existing entry → 5 total, one entry.
        Assert.Equal(5m, detail.Supply.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(5m, entry.Quantity);

        // Two quantity-1 conversion inflows, one per collapsed instance, with distinct related ids.
        var inflows = (await GetMovementsAsync(client, supply.Id))
            .Where(m => m.Kind == "Conversion" && m.ToAreaId != null && m.FromAreaId == null)
            .ToList();
        Assert.Equal(2, inflows.Count);
        Assert.All(inflows, m => Assert.Equal(1m, m.Quantity));
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x),
            inflows.Select(m => m.RelatedItemId!.Value).OrderBy(x => x));

        // Both instances are archived, keeping their location (terminal-lock mirror).
        var archivedA = await GetItemAsync(client, a.Id);
        Assert.Equal("Archived", archivedA.Status);
        Assert.Equal(areaId, archivedA.AreaId);
    }

    [Fact]
    public async Task Collapse_IntoNewLocation_CreatesEntry()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId = garageId, quantity = 2m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId = garageId, name = "One" }))).Item;

        var detail = await OkDetailAsync(await CollapseAsync(client, supply.Id,
            new { itemIds = new[] { item.Id }, areaId = pantryId }));

        var pantry = detail.Locations.First(l => l.AreaId == pantryId);
        Assert.Equal(1m, pantry.Quantity);
    }

    [Fact]
    public async Task Collapse_EmptyItemIds_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = Array.Empty<Guid>(), areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_NullItemIds_Returns400NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        // Explicit null reaches the validator (STJ's `required` enforcement only catches outright
        // omission). The duplicate-check must not deref the null list and surface as a raw 500.
        var resp = await CollapseAsync(client, supply.Id, new { itemIds = (Guid[]?)null, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_DuplicateItemIds_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "One" }))).Item;

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { item.Id, item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_UnknownItemId_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { Guid.NewGuid() }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_FungibleInSelection_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "One" }))).Item;

        // The supply itself is fungible — it cannot be collapsed.
        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { item.Id, supply.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_LentItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Lent out" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{item.Id}", new object[]
        {
            new { op = "replace", path = "/status", value = (object)"Lent" },
            new { op = "replace", path = "/lentTo", value = (object)"A neighbour" },
        });

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_AlreadyArchivedItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Archive me" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ContainerWithLiveChild_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        var container = await CreateContainerAsync(client, "Tool chest", areaId: areaId);
        await CreatePlainItemAsync(client, "A wrench", parentItemId: container.Id);

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { container.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ContainerWithStockInside_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        var container = await CreateContainerAsync(client, "Bin", areaId: areaId);
        // Put fungible stock inside the container so it is non-empty.
        await ReceiveAsync(client, supply.Id, new { containerItemId = container.Id, quantity = 4m });

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { container.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_EmptyContainer_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        var container = await CreateContainerAsync(client, "Empty box", areaId: areaId);

        var detail = await OkDetailAsync(await CollapseAsync(client, supply.Id,
            new { itemIds = new[] { container.Id }, areaId }));

        Assert.Equal(1m, detail.Supply.TotalOnHand);
        var archived = await GetItemAsync(client, container.Id);
        Assert.Equal("Archived", archived.Status);
    }

    [Fact]
    public async Task Collapse_TargetIsCollapsedItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        var container = await CreateContainerAsync(client, "Self box", areaId: areaId);

        var resp = await CollapseAsync(client, supply.Id,
            new { itemIds = new[] { container.Id }, containerItemId = container.Id });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ArchivedSupply_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "One" }))).Item;
        await ArchiveSupplyAsync(client, supply.Id);

        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_OneBadItem_IsAtomic()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var good = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Good" }))).Item;
        var lent = (await OkTrackAsync(await TrackOneAsync(client, supply.Id, new { areaId, name = "Lent" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{lent.Id}", new object[]
        {
            new { op = "replace", path = "/status", value = (object)"Lent" },
            new { op = "replace", path = "/lentTo", value = (object)"A neighbour" },
        });

        var before = await GetSupplyAsync(client, supply.Id);
        var movementsBefore = (await GetMovementsAsync(client, supply.Id)).Count;

        // One Lent item in the batch fails a pre-mutation check — the whole collapse rolls back.
        var resp = await CollapseAsync(client, supply.Id, new { itemIds = new[] { good.Id, lent.Id }, areaId });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var after = await GetSupplyAsync(client, supply.Id);
        Assert.Equal(before.Supply.TotalOnHand, after.Supply.TotalOnHand);
        Assert.Equal(movementsBefore, (await GetMovementsAsync(client, supply.Id)).Count);
        Assert.Equal("Active", (await GetItemAsync(client, good.Id)).Status);
    }
}
