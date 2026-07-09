using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record ConsumableItem(
    Guid Id, string Name, string Status, string? Unit, decimal? RestockThreshold,
    decimal TotalOnHand, bool IsLow, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<StockEntryItem>? StockLocations,
    // Trailing + defaulted: absent `tags` binds to null, so existing tag-agnostic tests are unaffected.
    IReadOnlyList<ItemTagRef>? Tags = null);

internal record StockEntryItem(
    Guid Id, decimal Quantity, Guid? AreaId, Guid? ContainerItemId,
    IReadOnlyList<LocationSegmentItem> Path, string? LocationLabel);

internal record ConsumableDetailItem(ConsumableItem Consumable, IReadOnlyList<StockEntryItem> Locations);

internal record StockMovementItem(
    Guid Id, string Kind, decimal Quantity,
    Guid? FromAreaId, Guid? FromContainerItemId, string? FromLabel,
    Guid? ToAreaId, Guid? ToContainerItemId, string? ToLabel,
    decimal? UnitCost, Guid? UnitCostCurrencyId, string? UnitCostCurrencySymbol,
    DateTimeOffset OccurredAt, Guid? RelatedItemId, string? RelatedItemName,
    Guid? CreatedByUserId, string? CreatedByDisplayName, string? Note, DateTimeOffset CreatedAt);

[Collection("Integration")]
public class ConsumablesEndpointTests : IntegrationTestBase
{
    public ConsumablesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<ConsumableItem> CreateConsumableAsync(
        HttpClient client, string name = "Screws", string? unit = null, decimal? threshold = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name, unit, restockThreshold = threshold }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name, Guid? parentAreaId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name, parentAreaId }, CT);
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

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/receive", body, CT);

    private static Task<HttpResponseMessage> ConsumeAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/consume", body, CT);

    private static Task<HttpResponseMessage> TransferAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/transfer", body, CT);

    private static Task<HttpResponseMessage> AdjustAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/adjust", body, CT);

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

    private static async Task<IReadOnlyList<StockMovementItem>> GetMovementsAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables/{id}/movements?page=1&pageSize=100", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<PaginatedResult<StockMovementItem>>(CT);
        return page!.Items;
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

    // ----- Fungible CRUD -----

    [Fact]
    public async Task CreateConsumable_AppliesFungibleActiveDefaults()
    {
        var client = await CreateAdminClientAsync();

        var consumable = await CreateConsumableAsync(client, "Paper towels", "rolls");

        Assert.Equal("Paper towels", consumable.Name);
        Assert.Equal("rolls", consumable.Unit);
        Assert.Equal("Active", consumable.Status);
        Assert.Equal(0m, consumable.TotalOnHand);
        Assert.False(consumable.IsLow);
    }

    [Fact]
    public async Task CreateConsumable_WithoutUnit_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        // Omit the unit entirely — this is what the create dialog sends when the field is left blank.
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Batteries" }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        Assert.Equal("Batteries", created.Name);
        // A missing unit is stored as NULL — the frontend supplies the display fallback.
        Assert.Null(created.Unit);
    }

    [Fact]
    public async Task CreateConsumable_NoStockFields_StartsEmpty()
    {
        var client = await CreateAdminClientAsync();

        // Regression: a bare create (no starting location/quantity) still produces a placeless,
        // zero-stock consumable — the deliberate "register the type, stock it later" path.
        var created = await CreateConsumableAsync(client, "Bolts", "boxes");

        var detail = await GetConsumableAsync(client, created.Id);
        Assert.Equal(0m, detail.Consumable.TotalOnHand);
        Assert.Empty(detail.Locations);
        Assert.Empty(await GetMovementsAsync(client, created.Id));
    }

    [Fact]
    public async Task CreateConsumable_WithStartingQuantityInArea_SeedsStockAndInflow()
    {
        var client = await CreateAdminClientAsync();
        var pantryId = await CreateAreaAsync(client, "Pantry");

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Batteries", unit = "cells", initialQuantity = 12m, initialAreaId = pantryId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        // The create response already reflects the seeded stock (it sums the new stock entry).
        Assert.Equal(12m, created.TotalOnHand);

        var detail = await GetConsumableAsync(client, created.Id);
        Assert.Equal(12m, detail.Consumable.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(12m, entry.Quantity);
        Assert.Equal(pantryId, entry.AreaId);

        var movement = Assert.Single(await GetMovementsAsync(client, created.Id));
        Assert.Equal("Flow", movement.Kind);
        Assert.Equal(12m, movement.Quantity);
        Assert.Null(movement.FromAreaId);
        Assert.Equal(pantryId, movement.ToAreaId);
    }

    [Fact]
    public async Task CreateConsumable_WithStartingQuantityInContainer_SeedsStockInContainer()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Screws", initialQuantity = 25m, initialContainerItemId = bin.Id }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;

        var detail = await GetConsumableAsync(client, created.Id);
        Assert.Equal(25m, detail.Consumable.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(25m, entry.Quantity);
        Assert.Equal(bin.Id, entry.ContainerItemId);
        Assert.Null(entry.AreaId);

        var movement = Assert.Single(await GetMovementsAsync(client, created.Id));
        Assert.Equal("Flow", movement.Kind);
        Assert.Equal(bin.Id, movement.ToContainerItemId);
    }

    [Fact]
    public async Task CreateConsumable_WithNonexistentStartingArea_RollsBackEntireCommand()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Ghost stock", initialQuantity = 5m, initialAreaId = Guid.NewGuid() }, CT);

        // The seed receive fails on the missing area, rolling back the whole command — no orphan.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var list = await (await client.GetAsync("/api/v1/inventory/consumables?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);
        Assert.DoesNotContain(list!.Items, s => s.Name == "Ghost stock");
    }

    [Fact]
    public async Task CreateConsumable_WithNonContainerStartingLocation_RollsBackEntireCommand()
    {
        var client = await CreateAdminClientAsync();
        // A plain (non-container) durable is not a valid stock location.
        var plainResp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name = "Hammer" }, CT);
        var plain = (await plainResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Misplaced stock", initialQuantity = 5m, initialContainerItemId = plain.Id }, CT);

        // The seed receive rejects the non-container location, rolling back the whole command.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var list = await (await client.GetAsync("/api/v1/inventory/consumables?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);
        Assert.DoesNotContain(list!.Items, s => s.Name == "Misplaced stock");
    }

    [Fact]
    public async Task PatchConsumable_EditsMetadata()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client);

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/consumables/{consumable.Id}",
            new object[]
            {
                new { op = "replace", path = "/name", value = (object)"Wood screws" },
                new { op = "replace", path = "/unit", value = (object)"boxes" },
                new { op = "replace", path = "/restockThreshold", value = (object)10m },
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        Assert.Equal("Wood screws", patched.Name);
        Assert.Equal("boxes", patched.Unit);
        Assert.Equal(10m, patched.RestockThreshold);
    }

    [Fact]
    public async Task PatchConsumable_ClearingUnit_StoresNull()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client, "Tape", "rolls");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/consumables/{consumable.Id}",
            new[] { new { op = "replace", path = "/unit", value = (object)"" } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        // A blank unit clears to NULL rather than persisting an empty string.
        Assert.Null(patched.Unit);
    }

    [Fact]
    public async Task PatchConsumable_NonFungibleStatus_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client);

        // Lent is meaningless for a count — only Active/Archived are valid for a consumable.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/consumables/{consumable.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Lent" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetConsumables_ExcludesArchivedUnlessIncludeInactive()
    {
        var client = await CreateAdminClientAsync();
        var live = await CreateConsumableAsync(client, "Live tape", "rolls");
        var archived = await CreateConsumableAsync(client, "Old tape", "rolls");
        await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{archived.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var defaultList = await (await client.GetAsync("/api/v1/inventory/consumables?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);
        Assert.Contains(defaultList!.Items, s => s.Id == live.Id);
        Assert.DoesNotContain(defaultList.Items, s => s.Id == archived.Id);

        var inactiveList = await (await client.GetAsync(
                "/api/v1/inventory/consumables?page=1&pageSize=200&includeInactive=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);
        Assert.Contains(inactiveList!.Items, s => s.Id == archived.Id);
    }

    [Fact]
    public async Task GetConsumables_LowStockOnly_FiltersToBelowThreshold()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Pantry");
        var low = await CreateConsumableAsync(client, "Low batteries", "cells", threshold: 5m);
        var ample = await CreateConsumableAsync(client, "Ample batteries", "cells", threshold: 5m);
        await ReceiveAsync(client, low.Id, new { areaId, quantity = 3m });
        await ReceiveAsync(client, ample.Id, new { areaId, quantity = 20m });

        var lowList = await (await client.GetAsync(
                "/api/v1/inventory/consumables?page=1&pageSize=200&lowStockOnly=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);

        Assert.Contains(lowList!.Items, s => s.Id == low.Id && s.IsLow);
        Assert.DoesNotContain(lowList.Items, s => s.Id == ample.Id);
    }

    [Fact]
    public async Task GetConsumables_ReturnsResolvedStockLocations()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 10m });
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 4m });

        var list = await (await client.GetAsync("/api/v1/inventory/consumables?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);

        var listed = list!.Items.Single(s => s.Id == consumable.Id);
        Assert.NotNull(listed.StockLocations);
        Assert.Equal(2, listed.StockLocations!.Count);

        // Ordered largest quantity first, exactly like the detail breakdown.
        var areaEntry = listed.StockLocations[0];
        Assert.Equal(10m, areaEntry.Quantity);
        Assert.Equal(garageId, areaEntry.AreaId);
        Assert.Equal("Garage", areaEntry.LocationLabel);
        var seg = Assert.Single(areaEntry.Path);
        Assert.Equal("Area", seg.Kind);
        Assert.Equal(garageId, seg.Id);

        // Stock in a nested container resolves the full root-area → container path.
        var containerEntry = listed.StockLocations[1];
        Assert.Equal(4m, containerEntry.Quantity);
        Assert.Equal(bin.Id, containerEntry.ContainerItemId);
        Assert.Null(containerEntry.AreaId);
        Assert.Equal("Garage › Parts bin", containerEntry.LocationLabel);
        Assert.Equal(2, containerEntry.Path.Count);
        Assert.Equal("Area", containerEntry.Path[0].Kind);
        Assert.Equal(garageId, containerEntry.Path[0].Id);
    }

    [Fact]
    public async Task DeleteConsumable_NoHistory_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client);

        var resp = await client.DeleteAsync($"/api/v1/inventory/consumables/{consumable.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteConsumable_WithHistory_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/consumables/{consumable.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Receive / merge / on-hand -----

    [Fact]
    public async Task Receive_CreatesEntryAndInflowMovement()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var detail = await OkDetailAsync(await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 12m }));

        Assert.Equal(12m, detail.Consumable.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(12m, entry.Quantity);
        Assert.Equal(areaId, entry.AreaId);
        Assert.Equal("Garage", entry.LocationLabel);

        var movements = await GetMovementsAsync(client, consumable.Id);
        var m = Assert.Single(movements);
        Assert.Equal("Flow", m.Kind);
        Assert.Equal(12m, m.Quantity);
        Assert.Null(m.FromAreaId);
        Assert.Equal(areaId, m.ToAreaId);
        Assert.Equal("Garage", m.ToLabel);
    }

    [Fact]
    public async Task Receive_SameLocationTwice_MergesIntoOneEntry()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        var detail = await OkDetailAsync(await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 3m }));

        // NULLS NOT DISTINCT unique index makes the second receive merge, not insert a second row.
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(8m, entry.Quantity);
        Assert.Equal(8m, detail.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task Receive_IntoContainer_TracksContainerLocation()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);

        var detail = await OkDetailAsync(await ReceiveAsync(client, consumable.Id,
            new { containerItemId = bin.Id, quantity = 7m }));

        var entry = Assert.Single(detail.Locations);
        Assert.Equal(bin.Id, entry.ContainerItemId);
        Assert.Null(entry.AreaId);
        // Path resolves root area → holding container.
        Assert.Equal("Garage › Parts bin", entry.LocationLabel);
    }

    [Fact]
    public async Task Receive_WithUnitCost_RecordsCostOnMovement()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var currencyId = await GetCurrencyIdAsync();
        var consumable = await CreateConsumableAsync(client);

        await ReceiveAsync(client, consumable.Id,
            new { areaId, quantity = 4m, unitCost = 2.5m, unitCostCurrencyId = currencyId });

        var m = Assert.Single(await GetMovementsAsync(client, consumable.Id));
        Assert.Equal(2.5m, m.UnitCost);
        Assert.Equal(currencyId, m.UnitCostCurrencyId);
        Assert.NotNull(m.UnitCostCurrencySymbol);
    }

    [Fact]
    public async Task Receive_CostWithoutCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var resp = await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 4m, unitCost = 2.5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_MissingArea_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client);

        var resp = await ReceiveAsync(client, consumable.Id, new { areaId = Guid.NewGuid(), quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_LocationIsNonContainerItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var plainResp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name = "Hammer" }, CT);
        var plain = (await plainResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
        var consumable = await CreateConsumableAsync(client);

        var resp = await ReceiveAsync(client, consumable.Id, new { containerItemId = plain.Id, quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_BothLocations_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: areaId);
        var consumable = await CreateConsumableAsync(client);

        var resp = await ReceiveAsync(client, consumable.Id,
            new { areaId, containerItemId = bin.Id, quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task StockOp_OnNonFungibleItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var itemResp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name = "Drill" }, CT);
        var item = (await itemResp.Content.ReadFromJsonAsync<DurableItem>(CT))!;

        var resp = await ReceiveAsync(client, item.Id, new { areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Consume -----

    [Fact]
    public async Task Consume_ReducesQuantityAndRecordsOutflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 10m });

        var detail = await OkDetailAsync(await ConsumeAsync(client, consumable.Id, new { areaId, quantity = 4m }));

        Assert.Equal(6m, detail.Consumable.TotalOnHand);
        var movements = await GetMovementsAsync(client, consumable.Id);
        var consume = movements.First(m => m.FromAreaId != null && m.ToAreaId == null);
        Assert.Equal("Flow", consume.Kind);
        Assert.Equal(4m, consume.Quantity);
        Assert.Equal(areaId, consume.FromAreaId);
        Assert.Null(consume.ToAreaId);
    }

    [Fact]
    public async Task Consume_All_RemovesEntry()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var detail = await OkDetailAsync(await ConsumeAsync(client, consumable.Id, new { areaId, quantity = 5m }));

        // A zero-quantity entry is deleted, not kept at zero.
        Assert.Empty(detail.Locations);
        Assert.Equal(0m, detail.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task Consume_MoreThanOnHand_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 3m });

        var resp = await ConsumeAsync(client, consumable.Id, new { areaId, quantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Consume_FromEmptyLocation_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var resp = await ConsumeAsync(client, consumable.Id, new { areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Transfer -----

    [Fact]
    public async Task Transfer_MovesBetweenLocations_OneMovementNetsZero()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 10m });

        var detail = await OkDetailAsync(await TransferAsync(client, consumable.Id,
            new { fromAreaId = garageId, toAreaId = pantryId, quantity = 4m }));

        Assert.Equal(10m, detail.Consumable.TotalOnHand); // transfer nets to zero on total
        var garage = detail.Locations.First(l => l.AreaId == garageId);
        var pantry = detail.Locations.First(l => l.AreaId == pantryId);
        Assert.Equal(6m, garage.Quantity);
        Assert.Equal(4m, pantry.Quantity);

        var movements = await GetMovementsAsync(client, consumable.Id);
        var transfer = movements.First(m => m.FromAreaId != null && m.ToAreaId != null);
        Assert.Equal("Flow", transfer.Kind);
        Assert.Equal(garageId, transfer.FromAreaId);
        Assert.Equal(pantryId, transfer.ToAreaId);
    }

    [Fact]
    public async Task Transfer_DrainingSource_RemovesSourceEntry()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 5m });

        var detail = await OkDetailAsync(await TransferAsync(client, consumable.Id,
            new { fromAreaId = garageId, toAreaId = pantryId, quantity = 5m }));

        var entry = Assert.Single(detail.Locations);
        Assert.Equal(pantryId, entry.AreaId);
        Assert.Equal(5m, entry.Quantity);
    }

    [Fact]
    public async Task Transfer_SameSourceAndDestination_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var resp = await TransferAsync(client, consumable.Id,
            new { fromAreaId = areaId, toAreaId = areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_MoreThanSource_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 2m });

        var resp = await TransferAsync(client, consumable.Id,
            new { fromAreaId = garageId, toAreaId = pantryId, quantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Adjust -----

    [Fact]
    public async Task Adjust_SetsAbsoluteAndRecordsAdjustment()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 10m });

        // Recount found fewer — corrects down to 7, recording an outflow Adjustment of 3.
        var detail = await OkDetailAsync(await AdjustAsync(client, consumable.Id, new { areaId, newQuantity = 7m }));
        Assert.Equal(7m, detail.Consumable.TotalOnHand);

        var movements = await GetMovementsAsync(client, consumable.Id);
        var adj = movements.First(m => m.Kind == "Adjustment");
        Assert.Equal(3m, adj.Quantity);
        Assert.Equal(areaId, adj.FromAreaId);
        Assert.Null(adj.ToAreaId);
    }

    [Fact]
    public async Task Adjust_ToZero_RemovesEntry()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 4m });

        var detail = await OkDetailAsync(await AdjustAsync(client, consumable.Id, new { areaId, newQuantity = 0m }));

        Assert.Empty(detail.Locations);
        Assert.Equal(0m, detail.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task Adjust_NewLocation_CreatesEntryAsInflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);

        var detail = await OkDetailAsync(await AdjustAsync(client, consumable.Id, new { areaId, newQuantity = 6m }));

        var entry = Assert.Single(detail.Locations);
        Assert.Equal(6m, entry.Quantity);
        var adj = (await GetMovementsAsync(client, consumable.Id)).First(m => m.Kind == "Adjustment");
        Assert.Equal(6m, adj.Quantity);
        Assert.Equal(areaId, adj.ToAreaId);
    }

    [Fact]
    public async Task Adjust_NoChange_Returns200NoOp()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        // A confirming recount (new == current) changes nothing: a 200 no-op, no Adjustment appended —
        // consistent with the module's net-zero-writes-nothing stance.
        var detail = await OkDetailAsync(await AdjustAsync(client, consumable.Id, new { areaId, newQuantity = 5m }));
        Assert.Equal(5m, detail.Consumable.TotalOnHand);

        // Only the seed inflow remains in the ledger — no Adjustment row was written.
        var movements = await GetMovementsAsync(client, consumable.Id);
        Assert.Single(movements);
        Assert.DoesNotContain(movements, m => m.Kind == "Adjustment");
    }

    // ----- Location-deletion gates -----

    [Fact]
    public async Task DeleteArea_WithStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/areas/{areaId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteContainer_HoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/durables/{bin.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchContainer_RoleOffWhileHoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{bin.Id}",
            new[] { new { op = "replace", path = "/durableKind", value = (object)"Plain" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SealContainer_WhileHoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{bin.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Ledger survives a deleted location -----

    [Fact]
    public async Task Movements_RenderGracefully_AfterLocationDeleted()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Temp area");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m });
        await ConsumeAsync(client, consumable.Id, new { areaId, quantity = 5m }); // empties the area

        // With no live stock or items, the area can be deleted; its ledger references SET NULL.
        var delete = await client.DeleteAsync($"/api/v1/inventory/areas/{areaId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var movements = await GetMovementsAsync(client, consumable.Id);
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Null(m.FromAreaId);
            Assert.Null(m.ToAreaId);
            Assert.Null(m.FromLabel);
            Assert.Null(m.ToLabel);
        });
    }

    // ----- Cross-endpoint robustness -----

    [Fact]
    public async Task DeleteItem_OnFungibleConsumableId_Returns404NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 5m }); // gives it ledger history

        // The non-fungible items endpoint must treat a consumable id as "not found", not reach Remove
        // and trip the StockMovement Restrict FK as a raw 500.
        var resp = await client.DeleteAsync($"/api/v1/inventory/durables/{consumable.Id}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        // The consumable is untouched and still deletes (and rejects) through its own endpoint.
        var stillThere = await GetConsumableAsync(client, consumable.Id);
        Assert.Equal(5m, stillThere.Consumable.TotalOnHand);
    }

    [Fact]
    public async Task StockOp_NoteTooLong_Returns400NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var consumable = await CreateConsumableAsync(client);
        var longNote = new string('x', 1001);

        var resp = await ReceiveAsync(client, consumable.Id, new { areaId, quantity = 1m, note = longNote });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- B1: a terminal container cannot be a stock destination; sources still work (escape hatch) -----

    [Fact]
    public async Task Receive_IntoArchivedContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        // Archiving the empty container is allowed; receiving stock into it is not.
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{bin.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        var consumable = await CreateConsumableAsync(client);

        var resp = await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_ToTerminalContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{bin.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { areaId = garageId, quantity = 10m });

        var resp = await TransferAsync(client, consumable.Id,
            new { fromAreaId = garageId, toContainerItemId = bin.Id, quantity = 3m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // The API can't seal a container that holds stock, so strand it directly — this is the state the
    // source escape hatch (and the adjust-down reconcile) exist for.
    private async Task ArchiveContainerDirectAsync(Guid containerId)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var container = await db.Items.FirstAsync(i => i.Id == containerId, CT);
        container.Status = ItemStatus.Archived;
        await db.SaveChangesAsync(CT);
    }

    [Fact]
    public async Task Consume_FromTerminalContainer_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });

        // Consuming stock OUT of the now-terminal container must still work.
        await ArchiveContainerDirectAsync(bin.Id);

        var resp = await ConsumeAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 2m });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_UpIntoTerminalContainer_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });
        await ArchiveContainerDirectAsync(bin.Id);

        // Adjusting UP (5 -> 10) is an inflow into a terminal container — blocked like a receive.
        var resp = await AdjustAsync(client, consumable.Id, new { containerItemId = bin.Id, newQuantity = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_DownInTerminalContainer_Succeeds()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var consumable = await CreateConsumableAsync(client);
        await ReceiveAsync(client, consumable.Id, new { containerItemId = bin.Id, quantity = 5m });
        await ArchiveContainerDirectAsync(bin.Id);

        // Adjusting DOWN (5 -> 2) reconciles stranded stock out — the escape hatch stays open.
        var resp = await AdjustAsync(client, consumable.Id, new { containerItemId = bin.Id, newQuantity = 2m });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ----- B8: ledger paging is stable across same-instant rows -----

    private static async Task<PaginatedResult<StockMovementItem>> GetMovementsPageAsync(
        HttpClient client, Guid id, int page, int pageSize)
    {
        var resp = await client.GetAsync(
            $"/api/v1/inventory/consumables/{id}/movements?page={page}&pageSize={pageSize}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PaginatedResult<StockMovementItem>>(CT))!;
    }

    [Fact]
    public async Task GetMovements_SameInstantRows_PageWithoutDuplicateOrSkip()
    {
        var client = await CreateAdminClientAsync();
        var consumable = await CreateConsumableAsync(client);

        // The API stamps its own UtcNow, so it can't produce a tie. Insert three movements that share
        // the exact same OccurredAt AND CreatedAt directly, so only the Id tiebreaker separates them;
        // without it, offset paging over the identical sort keys can repeat or skip a row at the boundary.
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        List<Guid> expectedOrder;
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in ids)
            {
                db.StockMovements.Add(new StockMovement
                {
                    Id = id,
                    ItemId = consumable.Id,
                    Kind = MovementKind.Flow,
                    Quantity = 1m,
                    OccurredAt = instant,
                    CreatedAt = instant,
                });
            }
            await db.SaveChangesAsync(CT);

            // The order the handler MUST produce, computed by the database with the same tiebreaker —
            // so the reference is Postgres's uuid ordering, not .NET's (different) Guid ordering. With
            // all three rows tied on OccurredAt+CreatedAt, this is a total order by Id descending.
            expectedOrder = await db.StockMovements.AsNoTracking()
                .Where(m => m.ItemId == consumable.Id)
                .OrderByDescending(m => m.OccurredAt)
                .ThenByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .Select(m => m.Id)
                .ToListAsync(CT);
        }

        var page1 = await GetMovementsPageAsync(client, consumable.Id, page: 1, pageSize: 2);
        var page2 = await GetMovementsPageAsync(client, consumable.Id, page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        var seen = page1.Items.Concat(page2.Items).Select(m => m.Id).ToList();
        // Exact sequence, not just set membership: without the .ThenByDescending(m => m.Id) tiebreaker
        // the tied rows come back in an unspecified scan order (≠ Id-descending for random Guids), so
        // this equality — and thus the no-duplicate/no-skip partition across the page boundary — only
        // holds once the tiebreaker makes the order total and stable.
        Assert.Equal(expectedOrder, seen);
    }

    // ----- Resolved-area list filtering (?areaId / ?unplaced) -----

    private static async Task<PaginatedResult<ConsumableItem>?> ListConsumablesAsync(HttpClient client, string query)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/consumables?{query}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<PaginatedResult<ConsumableItem>>(CT);
    }

    [Fact]
    public async Task GetConsumables_AreaFilter_MatchesStockResolvingIntoAreaOrSubtree()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var benchId = await CreateAreaAsync(client, "Workbench", parentAreaId: garageId);
        var officeId = await CreateAreaAsync(client, "Office");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);

        // Stock inside a nested container that resolves to the garage.
        var screws = await CreateConsumableAsync(client, "Screws");
        await ReceiveAsync(client, screws.Id, new { containerItemId = chest.Id, quantity = 4m });
        // Stock in a sub-area of the garage — matched by the parent-area filter (descendant rollup).
        var nails = await CreateConsumableAsync(client, "Nails");
        await ReceiveAsync(client, nails.Id, new { areaId = benchId, quantity = 20m });
        // Stock in an unrelated area — not matched.
        var clips = await CreateConsumableAsync(client, "Clips");
        await ReceiveAsync(client, clips.Id, new { areaId = officeId, quantity = 5m });

        var list = await ListConsumablesAsync(client, $"page=1&pageSize=200&areaId={garageId}");

        Assert.Contains(list!.Items, s => s.Id == screws.Id);
        Assert.Contains(list.Items, s => s.Id == nails.Id);
        Assert.DoesNotContain(list.Items, s => s.Id == clips.Id);
    }

    [Fact]
    public async Task GetConsumables_AreaFilter_CountsMultiLocationConsumableOnce()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateContainerAsync(client, "Tool chest", areaId: garageId);
        var screws = await CreateConsumableAsync(client, "Screws");
        await ReceiveAsync(client, screws.Id, new { areaId = garageId, quantity = 6m });      // resolves to garage
        await ReceiveAsync(client, screws.Id, new { containerItemId = chest.Id, quantity = 4m }); // also garage

        var list = await ListConsumablesAsync(client, $"page=1&pageSize=200&areaId={garageId}");

        Assert.Single(list!.Items, s => s.Id == screws.Id); // counted once despite two matching locations
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task GetConsumables_Unplaced_MatchesStockWithNoAreaRoot()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var floatingBin = await CreateContainerAsync(client, "Floating bin"); // no area
        var washers = await CreateConsumableAsync(client, "Washers");
        await ReceiveAsync(client, washers.Id, new { containerItemId = floatingBin.Id, quantity = 3m });
        var placed = await CreateConsumableAsync(client, "Screws");
        await ReceiveAsync(client, placed.Id, new { areaId = garageId, quantity = 6m });

        var list = await ListConsumablesAsync(client, "page=1&pageSize=200&unplaced=true");

        Assert.Contains(list!.Items, s => s.Id == washers.Id);
        Assert.DoesNotContain(list.Items, s => s.Id == placed.Id);
    }

    [Fact]
    public async Task GetConsumables_AreaFilter_ExcludesZeroStockConsumable()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var stocked = await CreateConsumableAsync(client, "Screws");
        await ReceiveAsync(client, stocked.Id, new { areaId = garageId, quantity = 6m });
        var empty = await CreateConsumableAsync(client, "Glue"); // never stocked — no locations

        var areaList = await ListConsumablesAsync(client, $"page=1&pageSize=200&areaId={garageId}");
        Assert.DoesNotContain(areaList!.Items, s => s.Id == empty.Id);

        var unplacedList = await ListConsumablesAsync(client, "page=1&pageSize=200&unplaced=true");
        Assert.DoesNotContain(unplacedList!.Items, s => s.Id == empty.Id);
    }

    [Fact]
    public async Task GetConsumables_AreaFilter_TotalCountReflectsFilteredSet()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");
        for (var i = 0; i < 3; i++)
        {
            var c = await CreateConsumableAsync(client, $"Garage stock {i}");
            await ReceiveAsync(client, c.Id, new { areaId = garageId, quantity = 1m });
        }
        var officePen = await CreateConsumableAsync(client, "Office pen");
        await ReceiveAsync(client, officePen.Id, new { areaId = officeId, quantity = 1m });

        var list = await ListConsumablesAsync(client, $"page=1&pageSize=2&areaId={garageId}");

        Assert.Equal(2, list!.Items.Count);   // one page
        Assert.Equal(3, list.TotalCount);     // whole filtered set, not the page or the office pen
    }

    // ----- Classification tags (create/patch whole-set-replace + TagIds facet) -----

    private static async Task<ConsumableItem> CreateConsumableWithTagsAsync(
        HttpClient client, string name, params string[] tags)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name, tags }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    // ItemTagRef binds the fields we assert on; the extra tag-list columns are ignored by System.Text.Json.
    private static async Task<IReadOnlyList<ItemTagRef>> ListTagsAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/inventory/tags?Page=1&PageSize=200", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PaginatedResult<ItemTagRef>>(CT))!.Items;
    }

    [Fact]
    public async Task CreateConsumable_WithTags_PopulatesResponseAndUpsertsAncestors()
    {
        var client = await CreateAdminClientAsync();

        var item = await CreateConsumableWithTagsAsync(client, "Screws", "Tools/Cordless");

        var badge = Assert.Single(item.Tags!);
        Assert.Equal("Cordless", badge.Name);
        Assert.Equal("Tools/Cordless", badge.Path);

        var tags = await ListTagsAsync(client);
        Assert.Contains(tags, t => t.Path == "Tools");
        Assert.Contains(tags, t => t.Path == "Tools/Cordless");

        var detail = await GetConsumableAsync(client, item.Id);
        Assert.Single(detail.Consumable.Tags!, t => t.Path == "Tools/Cordless");
    }

    [Fact]
    public async Task PatchConsumable_ReplaceTags_WholeSetReplace()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateConsumableWithTagsAsync(client, "Screws", "Old");
        Assert.Single(item.Tags!, t => t.Name == "Old");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{item.Id}",
            new[] { new { op = "replace", path = "/tags", value = (object)new[] { "New" } } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        Assert.Single(patched.Tags!, t => t.Name == "New");
        Assert.DoesNotContain(patched.Tags!, t => t.Name == "Old");
    }

    [Fact]
    public async Task PatchConsumable_NoTagsOp_LeavesTagsUntouched()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateConsumableWithTagsAsync(client, "Screws", "Keep");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{item.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Wood screws" } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        Assert.Equal("Wood screws", patched.Name);
        Assert.Single(patched.Tags!, t => t.Name == "Keep");
    }

    [Fact]
    public async Task PatchConsumable_ClearTags_EmptyArray()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateConsumableWithTagsAsync(client, "Screws", "Remove");

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{item.Id}",
            new[] { new { op = "replace", path = "/tags", value = (object)Array.Empty<string>() } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        Assert.Empty(patched.Tags!);
    }

    [Fact]
    public async Task GetConsumables_TagIdsFilter_Narrows()
    {
        var client = await CreateAdminClientAsync();
        var tagged = await CreateConsumableWithTagsAsync(client, "Tagged screws", "Facet");
        await CreateConsumableWithTagsAsync(client, "Plain screws");
        var facetId = tagged.Tags!.Single().Id;

        var list = await ListConsumablesAsync(client, $"page=1&pageSize=200&TagIds={facetId}");

        var only = Assert.Single(list!.Items);
        Assert.Equal(tagged.Id, only.Id);
    }

    [Fact]
    public async Task GetConsumables_IncludeChildTags_PullsDescendants()
    {
        var client = await CreateAdminClientAsync();
        var child = await CreateConsumableWithTagsAsync(client, "Child-tagged", "Parent/Child");
        var parentId = (await ListTagsAsync(client)).Single(t => t.Path == "Parent").Id;

        var without = await ListConsumablesAsync(client, $"page=1&pageSize=200&TagIds={parentId}");
        Assert.DoesNotContain(without!.Items, i => i.Id == child.Id);

        var with = await ListConsumablesAsync(client, $"page=1&pageSize=200&TagIds={parentId}&IncludeChildTags=true");
        Assert.Contains(with!.Items, i => i.Id == child.Id);
    }

    [Fact]
    public async Task GetConsumables_TagFilter_ComposesWithAreaFilter()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var officeId = await CreateAreaAsync(client, "Office");

        // Tagged with stock in the garage — the target of the composed filter.
        var inGarageResp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Garage screws", tags = new[] { "Facet" }, initialQuantity = 5m, initialAreaId = garageId }, CT);
        Assert.Equal(HttpStatusCode.Created, inGarageResp.StatusCode);
        var inGarage = (await inGarageResp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
        var facetId = inGarage.Tags!.Single().Id;

        // Same tag but stock in a different area — matched by tag alone, excluded once the area intersects.
        await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Office screws", tags = new[] { "Facet" }, initialQuantity = 5m, initialAreaId = officeId }, CT);
        // Untagged but in the garage — matched by area alone, excluded once the tag intersects.
        await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Garage nails", initialQuantity = 5m, initialAreaId = garageId }, CT);

        var list = await ListConsumablesAsync(client, $"page=1&pageSize=200&TagIds={facetId}&areaId={garageId}");

        var only = Assert.Single(list!.Items);
        Assert.Equal(inGarage.Id, only.Id);
    }
}
