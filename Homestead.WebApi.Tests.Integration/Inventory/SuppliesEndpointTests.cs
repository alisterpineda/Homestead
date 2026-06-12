using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record SupplyItem(
    Guid Id, string Name, string Status, string Unit, decimal? RestockThreshold,
    decimal TotalOnHand, bool IsLow, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<StockEntryItem>? StockLocations);

internal record StockEntryItem(
    Guid Id, decimal Quantity, Guid? AreaId, Guid? ContainerItemId,
    IReadOnlyList<LocationSegmentItem> Path, string? LocationLabel);

internal record SupplyDetailItem(SupplyItem Supply, IReadOnlyList<StockEntryItem> Locations);

internal record StockMovementItem(
    Guid Id, string Kind, decimal Quantity,
    Guid? FromAreaId, Guid? FromContainerItemId, string? FromLabel,
    Guid? ToAreaId, Guid? ToContainerItemId, string? ToLabel,
    decimal? UnitCost, Guid? UnitCostCurrencyId, string? UnitCostCurrencySymbol,
    DateTimeOffset OccurredAt, Guid? RelatedItemId, string? RelatedItemName,
    Guid? CreatedByUserId, string? CreatedByDisplayName, string? Note, DateTimeOffset CreatedAt);

[Collection("Integration")]
public class SuppliesEndpointTests : IntegrationTestBase
{
    public SuppliesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<SupplyItem> CreateSupplyAsync(
        HttpClient client, string name = "Screws", string unit = "screws", decimal? threshold = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/supplies",
            new { name, unit, restockThreshold = threshold }, CT);
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

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/receive", body, CT);

    private static Task<HttpResponseMessage> ConsumeAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/consume", body, CT);

    private static Task<HttpResponseMessage> TransferAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/transfer", body, CT);

    private static Task<HttpResponseMessage> AdjustAsync(HttpClient client, Guid supplyId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/adjust", body, CT);

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

    private static async Task<IReadOnlyList<StockMovementItem>> GetMovementsAsync(HttpClient client, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/supplies/{id}/movements?page=1&pageSize=100", CT);
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
    public async Task CreateSupply_AppliesFungibleActiveDefaults()
    {
        var client = await CreateAdminClientAsync();

        var supply = await CreateSupplyAsync(client, "Paper towels", "rolls");

        Assert.Equal("Paper towels", supply.Name);
        Assert.Equal("rolls", supply.Unit);
        Assert.Equal("Active", supply.Status);
        Assert.Equal(0m, supply.TotalOnHand);
        Assert.False(supply.IsLow);
    }

    [Fact]
    public async Task CreateSupply_MissingUnit_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync("/api/v1/inventory/supplies",
            new { name = "Nameless", unit = "" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchSupply_EditsMetadata()
    {
        var client = await CreateAdminClientAsync();
        var supply = await CreateSupplyAsync(client);

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/supplies/{supply.Id}",
            new object[]
            {
                new { op = "replace", path = "/name", value = (object)"Wood screws" },
                new { op = "replace", path = "/unit", value = (object)"boxes" },
                new { op = "replace", path = "/restockThreshold", value = (object)10m },
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = (await resp.Content.ReadFromJsonAsync<SupplyItem>(CT))!;
        Assert.Equal("Wood screws", patched.Name);
        Assert.Equal("boxes", patched.Unit);
        Assert.Equal(10m, patched.RestockThreshold);
    }

    [Fact]
    public async Task PatchSupply_NonFungibleStatus_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var supply = await CreateSupplyAsync(client);

        // Lent is meaningless for a count — only Active/Archived are valid for a supply.
        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/supplies/{supply.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Lent" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetSupplies_ExcludesArchivedUnlessIncludeInactive()
    {
        var client = await CreateAdminClientAsync();
        var live = await CreateSupplyAsync(client, "Live tape", "rolls");
        var archived = await CreateSupplyAsync(client, "Old tape", "rolls");
        await SendJsonPatchAsync(client, $"/api/v1/inventory/supplies/{archived.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var defaultList = await (await client.GetAsync("/api/v1/inventory/supplies?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<SupplyItem>>(CT);
        Assert.Contains(defaultList!.Items, s => s.Id == live.Id);
        Assert.DoesNotContain(defaultList.Items, s => s.Id == archived.Id);

        var inactiveList = await (await client.GetAsync(
                "/api/v1/inventory/supplies?page=1&pageSize=200&includeInactive=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<SupplyItem>>(CT);
        Assert.Contains(inactiveList!.Items, s => s.Id == archived.Id);
    }

    [Fact]
    public async Task GetSupplies_LowStockOnly_FiltersToBelowThreshold()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Pantry");
        var low = await CreateSupplyAsync(client, "Low batteries", "cells", threshold: 5m);
        var ample = await CreateSupplyAsync(client, "Ample batteries", "cells", threshold: 5m);
        await ReceiveAsync(client, low.Id, new { areaId, quantity = 3m });
        await ReceiveAsync(client, ample.Id, new { areaId, quantity = 20m });

        var lowList = await (await client.GetAsync(
                "/api/v1/inventory/supplies?page=1&pageSize=200&lowStockOnly=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<SupplyItem>>(CT);

        Assert.Contains(lowList!.Items, s => s.Id == low.Id && s.IsLow);
        Assert.DoesNotContain(lowList.Items, s => s.Id == ample.Id);
    }

    [Fact]
    public async Task GetSupplies_ReturnsResolvedStockLocations()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId = garageId, quantity = 10m });
        await ReceiveAsync(client, supply.Id, new { containerItemId = bin.Id, quantity = 4m });

        var list = await (await client.GetAsync("/api/v1/inventory/supplies?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<SupplyItem>>(CT);

        var listed = list!.Items.Single(s => s.Id == supply.Id);
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
    public async Task DeleteSupply_NoHistory_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var supply = await CreateSupplyAsync(client);

        var resp = await client.DeleteAsync($"/api/v1/inventory/supplies/{supply.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteSupply_WithHistory_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/supplies/{supply.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Receive / merge / on-hand -----

    [Fact]
    public async Task Receive_CreatesEntryAndInflowMovement()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var detail = await OkDetailAsync(await ReceiveAsync(client, supply.Id, new { areaId, quantity = 12m }));

        Assert.Equal(12m, detail.Supply.TotalOnHand);
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(12m, entry.Quantity);
        Assert.Equal(areaId, entry.AreaId);
        Assert.Equal("Garage", entry.LocationLabel);

        var movements = await GetMovementsAsync(client, supply.Id);
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
        var supply = await CreateSupplyAsync(client);

        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        var detail = await OkDetailAsync(await ReceiveAsync(client, supply.Id, new { areaId, quantity = 3m }));

        // NULLS NOT DISTINCT unique index makes the second receive merge, not insert a second row.
        var entry = Assert.Single(detail.Locations);
        Assert.Equal(8m, entry.Quantity);
        Assert.Equal(8m, detail.Supply.TotalOnHand);
    }

    [Fact]
    public async Task Receive_IntoContainer_TracksContainerLocation()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Parts bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);

        var detail = await OkDetailAsync(await ReceiveAsync(client, supply.Id,
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
        var supply = await CreateSupplyAsync(client);

        await ReceiveAsync(client, supply.Id,
            new { areaId, quantity = 4m, unitCost = 2.5m, unitCostCurrencyId = currencyId });

        var m = Assert.Single(await GetMovementsAsync(client, supply.Id));
        Assert.Equal(2.5m, m.UnitCost);
        Assert.Equal(currencyId, m.UnitCostCurrencyId);
        Assert.NotNull(m.UnitCostCurrencySymbol);
    }

    [Fact]
    public async Task Receive_CostWithoutCurrency_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var resp = await ReceiveAsync(client, supply.Id, new { areaId, quantity = 4m, unitCost = 2.5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_MissingArea_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var supply = await CreateSupplyAsync(client);

        var resp = await ReceiveAsync(client, supply.Id, new { areaId = Guid.NewGuid(), quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_LocationIsNonContainerItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var plainResp = await client.PostAsJsonAsync("/api/v1/inventory/items", new { name = "Hammer" }, CT);
        var plain = (await plainResp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
        var supply = await CreateSupplyAsync(client);

        var resp = await ReceiveAsync(client, supply.Id, new { containerItemId = plain.Id, quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Receive_BothLocations_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: areaId);
        var supply = await CreateSupplyAsync(client);

        var resp = await ReceiveAsync(client, supply.Id,
            new { areaId, containerItemId = bin.Id, quantity = 4m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task StockOp_OnNonFungibleItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var itemResp = await client.PostAsJsonAsync("/api/v1/inventory/items", new { name = "Drill" }, CT);
        var item = (await itemResp.Content.ReadFromJsonAsync<ItemItem>(CT))!;

        var resp = await ReceiveAsync(client, item.Id, new { areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Consume -----

    [Fact]
    public async Task Consume_ReducesQuantityAndRecordsOutflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 10m });

        var detail = await OkDetailAsync(await ConsumeAsync(client, supply.Id, new { areaId, quantity = 4m }));

        Assert.Equal(6m, detail.Supply.TotalOnHand);
        var movements = await GetMovementsAsync(client, supply.Id);
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
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var detail = await OkDetailAsync(await ConsumeAsync(client, supply.Id, new { areaId, quantity = 5m }));

        // A zero-quantity entry is deleted, not kept at zero.
        Assert.Empty(detail.Locations);
        Assert.Equal(0m, detail.Supply.TotalOnHand);
    }

    [Fact]
    public async Task Consume_MoreThanOnHand_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 3m });

        var resp = await ConsumeAsync(client, supply.Id, new { areaId, quantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Consume_FromEmptyLocation_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var resp = await ConsumeAsync(client, supply.Id, new { areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Transfer -----

    [Fact]
    public async Task Transfer_MovesBetweenLocations_OneMovementNetsZero()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId = garageId, quantity = 10m });

        var detail = await OkDetailAsync(await TransferAsync(client, supply.Id,
            new { fromAreaId = garageId, toAreaId = pantryId, quantity = 4m }));

        Assert.Equal(10m, detail.Supply.TotalOnHand); // transfer nets to zero on total
        var garage = detail.Locations.First(l => l.AreaId == garageId);
        var pantry = detail.Locations.First(l => l.AreaId == pantryId);
        Assert.Equal(6m, garage.Quantity);
        Assert.Equal(4m, pantry.Quantity);

        var movements = await GetMovementsAsync(client, supply.Id);
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
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId = garageId, quantity = 5m });

        var detail = await OkDetailAsync(await TransferAsync(client, supply.Id,
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
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await TransferAsync(client, supply.Id,
            new { fromAreaId = areaId, toAreaId = areaId, quantity = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_MoreThanSource_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId = garageId, quantity = 2m });

        var resp = await TransferAsync(client, supply.Id,
            new { fromAreaId = garageId, toAreaId = pantryId, quantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Adjust -----

    [Fact]
    public async Task Adjust_SetsAbsoluteAndRecordsAdjustment()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 10m });

        // Recount found fewer — corrects down to 7, recording an outflow Adjustment of 3.
        var detail = await OkDetailAsync(await AdjustAsync(client, supply.Id, new { areaId, newQuantity = 7m }));
        Assert.Equal(7m, detail.Supply.TotalOnHand);

        var movements = await GetMovementsAsync(client, supply.Id);
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
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 4m });

        var detail = await OkDetailAsync(await AdjustAsync(client, supply.Id, new { areaId, newQuantity = 0m }));

        Assert.Empty(detail.Locations);
        Assert.Equal(0m, detail.Supply.TotalOnHand);
    }

    [Fact]
    public async Task Adjust_NewLocation_CreatesEntryAsInflow()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);

        var detail = await OkDetailAsync(await AdjustAsync(client, supply.Id, new { areaId, newQuantity = 6m }));

        var entry = Assert.Single(detail.Locations);
        Assert.Equal(6m, entry.Quantity);
        var adj = (await GetMovementsAsync(client, supply.Id)).First(m => m.Kind == "Adjustment");
        Assert.Equal(6m, adj.Quantity);
        Assert.Equal(areaId, adj.ToAreaId);
    }

    [Fact]
    public async Task Adjust_NoChange_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await AdjustAsync(client, supply.Id, new { areaId, newQuantity = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Location-deletion gates -----

    [Fact]
    public async Task DeleteArea_WithStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/areas/{areaId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteContainer_HoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await client.DeleteAsync($"/api/v1/inventory/items/{bin.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchContainer_RoleOffWhileHoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{bin.Id}",
            new[] { new { op = "replace", path = "/isContainer", value = (object)false } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SealContainer_WhileHoldingStock_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var bin = await CreateContainerAsync(client, "Bin", areaId: garageId);
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { containerItemId = bin.Id, quantity = 5m });

        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{bin.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Ledger survives a deleted location -----

    [Fact]
    public async Task Movements_RenderGracefully_AfterLocationDeleted()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Temp area");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m });
        await ConsumeAsync(client, supply.Id, new { areaId, quantity = 5m }); // empties the area

        // With no live stock or items, the area can be deleted; its ledger references SET NULL.
        var delete = await client.DeleteAsync($"/api/v1/inventory/areas/{areaId}", CT);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var movements = await GetMovementsAsync(client, supply.Id);
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
    public async Task DeleteItem_OnFungibleSupplyId_Returns404NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        await ReceiveAsync(client, supply.Id, new { areaId, quantity = 5m }); // gives it ledger history

        // The non-fungible items endpoint must treat a supply id as "not found", not reach Remove
        // and trip the StockMovement Restrict FK as a raw 500.
        var resp = await client.DeleteAsync($"/api/v1/inventory/items/{supply.Id}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        // The supply is untouched and still deletes (and rejects) through its own endpoint.
        var stillThere = await GetSupplyAsync(client, supply.Id);
        Assert.Equal(5m, stillThere.Supply.TotalOnHand);
    }

    [Fact]
    public async Task StockOp_NoteTooLong_Returns400NotRawError()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var supply = await CreateSupplyAsync(client);
        var longNote = new string('x', 1001);

        var resp = await ReceiveAsync(client, supply.Id, new { areaId, quantity = 1m, note = longNote });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
