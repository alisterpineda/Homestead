using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// The track-one response shape: refreshed consumable detail + the freshly-created individual item.
internal record TrackDurableResultItem(ConsumableDetailItem Detail, DurableItem Item);

[Collection("Integration")]
public class ConversionsEndpointTests : IntegrationTestBase
{
    public ConversionsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<ConsumableItem> CreateConsumableAsync(
        HttpClient client, string name = "Screws", string unit = "screws")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name, unit }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static async Task<DurableItem> CreateContainerAsync(HttpClient client, string name, Guid? areaId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name, areaId, durableKind = "Container" }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static async Task<DurableItem> CreatePlainDurableAsync(
        HttpClient client, string name, Guid? areaId = null, Guid? parentItemId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name, areaId, parentItemId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/receive", body, CT);

    private static Task<HttpResponseMessage> TrackOneAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/track-one", body, CT);

    private static Task<HttpResponseMessage> CollapseAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/collapse", body, CT);

    private static Task<HttpResponseMessage> ConsumeAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/consume", body, CT);

    private static async Task<TrackDurableResultItem> OkTrackAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<TrackDurableResultItem>(CT))!;
    }

    private static async Task<ConsumableDetailItem> OkDetailAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableDetailItem>(CT))!;
    }

    private static async Task<ConsumableDetailItem> GetConsumableAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableDetailItem>(CT))!;
    }

    private static async Task<DurableItem> GetDurableAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/durables/{id}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static async Task<IReadOnlyList<StockMovementItem>> GetMovementsAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables/{id}/movements?page=1&pageSize=100", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PaginatedResult<StockMovementItem>>(CT);
        return page!.Items;
    }

    private static async Task ArchiveConsumableAsync(HttpClient client, Guid consumableId)
    {
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{consumableId}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ----- Track one individually -----

    [Fact]
    public async Task TrackOne_DecrementsCreatesItemAndConversionOutflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client, "Drill bits", "bits");
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var result = await OkTrackAsync(await TrackOneAsync(client, consumable.Id,
            new { areaId, name = "Drill bit #1" }));

        // Refreshed detail: stock dropped by exactly one.
        Assert.Equal(4m, result.Detail.Consumable.TotalOnHand);
        var entry = Assert.Single(result.Detail.Locations);
        Assert.Equal(4m, entry.Quantity);

        // The new individual item.
        Assert.Equal("Drill bit #1", result.Item.Name);
        Assert.Equal("NonFungible", result.Item.Fungibility);
        Assert.Equal("Active", result.Item.Status);
        Assert.Equal(areaId, result.Item.AreaId);
        Assert.Null(result.Item.ParentItemId);
        Assert.Equal("Plain", result.Item.DurableKind);

        // The conversion outflow carries the new item's id for two-way traceability.
        var conversions = (await GetMovementsAsync(client, consumable.Id)).Where(m => m.Kind == "Conversion").ToList();
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
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 3m });

        var result = await OkTrackAsync(await TrackOneAsync(client, consumable.Id,
            new { containerItemId = bin.Id, name = "Special screw", compartment = "Top tray" }));

        Assert.Equal(bin.Id, result.Item.ParentItemId);
        Assert.Null(result.Item.AreaId);
        Assert.Equal("Top tray", result.Item.Compartment);
        Assert.Equal(2m, result.Detail.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task TrackOne_LastUnit_RemovesEntry()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 1m });

        var result = await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Last one" }));

        // A zero-quantity entry is purged, not kept at zero.
        Assert.Empty(result.Detail.Locations);
        Assert.Equal(0m, result.Detail.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task TrackOne_FractionalQuantity_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client, "Rope", "m");
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 3.5m });

        var resp = await TrackOneAsync(client, consumable.Id, new { areaId, name = "A length" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_QuantityBelowOne_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client, "Rope", "m");
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 0.5m });

        var resp = await TrackOneAsync(client, consumable.Id, new { areaId, name = "A length" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_NoStockAtLocation_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var resp = await TrackOneAsync(client, consumable.Id, new { areaId, name = "Phantom" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_ArchivedConsumable_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        await ArchiveConsumableAsync(client, consumable.Id);

        var resp = await TrackOneAsync(client, consumable.Id, new { areaId, name = "Nope" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_OnNonFungibleItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var item = await CreatePlainDurableAsync(client, "Hammer", areaId: areaId);

        var resp = await TrackOneAsync(client, item.Id, new { areaId, name = "From a non-consumable" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_MissingName_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var resp = await TrackOneAsync(client, consumable.Id, new { areaId, name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task TrackOne_BothLocations_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: areaId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var resp = await TrackOneAsync(client, consumable.Id,
            new { areaId, containerItemId = bin.Id, name = "Ambiguous" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Collapse back to a count -----

    [Fact]
    public async Task Collapse_TwoItems_AddsTwoWithDistinctInflowsAndArchivesItems()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var a = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Screw A" }))).Item;
        var b = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Screw B" }))).Item;
        // Stock now 3 at the area; two individual items carved out.

        var detail = await OkDetailAsync(await CollapseAsync(client, consumable.Id,
            new { itemIds = new[] { a.Id, b.Id }, areaId }));

        // +2 merged back into the existing entry → 5 total, one entry.
        Assert.Equal(5m, detail.Consumable.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(5m, entry.Quantity);

        // Two quantity-1 conversion inflows, one per collapsed instance, with distinct related ids.
        var inflows = (await GetMovementsAsync(client, consumable.Id))
            .Where(m => m.Kind == "Conversion" && m.ToAreaId != null && m.FromAreaId == null)
            .ToList();
        Assert.Equal(2, inflows.Count);
        Assert.All(inflows, m => Assert.Equal(1m, m.Quantity));
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x),
            inflows.Select(m => m.RelatedItemId!.Value).OrderBy(x => x));

        // Both instances are archived, keeping their location (terminal-lock mirror).
        var archivedA = await GetDurableAsync(client, a.Id);
        Assert.Equal("Archived", archivedA.Status);
        Assert.Equal(areaId, archivedA.AreaId);
    }

    [Fact]
    public async Task Collapse_IntoNewLocation_CreatesEntry()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 2m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId = garageId, name = "One" }))).Item;

        var detail = await OkDetailAsync(await CollapseAsync(client, consumable.Id,
            new { itemIds = new[] { item.Id }, areaId = pantryId }));

        var pantry = detail.Locations.First(l => l.AreaId == pantryId);
        Assert.Equal(1m, pantry.Quantity);
    }

    [Fact]
    public async Task Collapse_EmptyItemIds_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = Array.Empty<Guid>(), areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_NullItemIds_Returns400NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        // Explicit null reaches the validator (STJ's `required` enforcement only catches outright
        // omission). The duplicate-check must not deref the null list and surface as a raw 500.
        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = (Guid[]?)null, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_DuplicateItemIds_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "One" }))).Item;

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { item.Id, item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_UnknownItemId_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { Guid.NewGuid() }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_FungibleInSelection_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "One" }))).Item;

        // The consumable itself is fungible — it cannot be collapsed.
        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { item.Id, consumable.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_LentItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Lent out" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{item.Id}", new object[]
        {
            new { op = "replace", path = "/status", value = (object)"Lent" },
            new { op = "replace", path = "/lentTo", value = (object)"A neighbour" },
        });

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_AlreadyArchivedItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Archive me" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ContainerWithLiveChild_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        var container = await CreateContainerAsync(client, "Tool chest", areaId: areaId);
        await CreatePlainDurableAsync(client, "A wrench", parentItemId: container.Id);

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { container.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ContainerWithStockInside_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        var container = await CreateContainerAsync(client, "Bin", areaId: areaId);
        // Put fungible stock inside the container so it is non-empty.
        await ReceiveAsync(client, consumable.Id, new { containerItemId = container.Id, quantity = 4m });

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { container.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_EmptyContainer_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        var container = await CreateContainerAsync(client, "Empty box", areaId: areaId);

        var detail = await OkDetailAsync(await CollapseAsync(client, consumable.Id,
            new { itemIds = new[] { container.Id }, areaId }));

        Assert.Equal(1m, detail.Consumable.TotalOnHand);
        var archived = await GetDurableAsync(client, container.Id);
        Assert.Equal("Archived", archived.Status);
    }

    [Fact]
    public async Task Collapse_TargetIsCollapsedItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        var container = await CreateContainerAsync(client, "Self box", areaId: areaId);

        var resp = await CollapseAsync(client, consumable.Id,
            new { itemIds = new[] { container.Id }, containerItemId = container.Id });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_ArchivedConsumable_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var item = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "One" }))).Item;
        await ArchiveConsumableAsync(client, consumable.Id);

        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { item.Id }, areaId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Collapse_OneBadItem_IsAtomic()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var good = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Good" }))).Item;
        var lent = (await OkTrackAsync(await TrackOneAsync(client, consumable.Id, new { areaId, name = "Lent" }))).Item;
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{lent.Id}", new object[]
        {
            new { op = "replace", path = "/status", value = (object)"Lent" },
            new { op = "replace", path = "/lentTo", value = (object)"A neighbour" },
        });

        var before = await GetConsumableAsync(client, consumable.Id);
        var movementsBefore = (await GetMovementsAsync(client, consumable.Id)).Count;

        // One Lent item in the batch fails a pre-mutation check — the whole collapse rolls back.
        var resp = await CollapseAsync(client, consumable.Id, new { itemIds = new[] { good.Id, lent.Id }, areaId });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var after = await GetConsumableAsync(client, consumable.Id);
        Assert.Equal(before.Consumable.TotalOnHand, after.Consumable.TotalOnHand);
        Assert.Equal(movementsBefore, (await GetMovementsAsync(client, consumable.Id)).Count);
        Assert.Equal("Active", (await GetDurableAsync(client, good.Id)).Status);
    }
}
