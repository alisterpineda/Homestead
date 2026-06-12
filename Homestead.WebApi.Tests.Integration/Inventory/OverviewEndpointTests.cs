using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record OverviewResult(
    int LowStockCount, IReadOnlyList<LowStockPreviewItem> LowStockSupplies,
    int LentCount, IReadOnlyList<LentPreviewItem> LentItems,
    int UnplacedCount, IReadOnlyList<UnplacedPreviewItem> UnplacedItems,
    IReadOnlyList<AreaSummaryItem> Areas);

internal record LowStockPreviewItem(
    Guid Id, string Name, decimal TotalOnHand, string Unit, decimal RestockThreshold);

internal record LentPreviewItem(Guid Id, string Name, string? LentTo);

internal record UnplacedPreviewItem(Guid Id, string Name);

internal record AreaSummaryItem(Guid Id, string Name, int ItemCount);

[Collection("Integration")]
public class OverviewEndpointTests : IntegrationTestBase
{
    public OverviewEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<OverviewResult> GetOverviewAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/inventory/overview", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<OverviewResult>(CT))!;
    }

    private static async Task<ItemItem> CreateItemAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    private static Task<ItemItem> CreateContainerAsync(
        HttpClient client, string name, Guid? areaId = null, Guid? parentItemId = null) =>
        CreateItemAsync(client, new { name, areaId, parentItemId, isContainer = true });

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static async Task<SupplyItem> CreateSupplyAsync(
        HttpClient client, string name, string unit = "units", decimal? threshold = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/supplies",
            new { name, unit, restockThreshold = threshold }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SupplyItem>(CT))!;
    }

    private static async Task ReceiveAsync(
        HttpClient client, Guid supplyId, Guid? areaId, decimal quantity, Guid? containerItemId = null)
    {
        var resp = await client.PostAsJsonAsync($"/api/v1/inventory/supplies/{supplyId}/receive",
            new { areaId, containerItemId, quantity }, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task PatchStatusAsync(
        HttpClient client, Guid itemId, string status, string? lentTo = null)
    {
        var ops = new List<object> { new { op = "replace", path = "/status", value = (object)status } };
        if (lentTo is not null)
            ops.Add(new { op = "replace", path = "/lentTo", value = (object)lentTo });
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{itemId}", ops.ToArray());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task PatchSupplyStatusAsync(HttpClient client, Guid supplyId, string status)
    {
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/supplies/{supplyId}",
            new[] { new { op = "replace", path = "/status", value = (object)status } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ----- Tests -----

    [Fact]
    public async Task Overview_EmptyHousehold_AllZero()
    {
        var client = await CreateAdminClientAsync();

        var overview = await GetOverviewAsync(client);

        Assert.Equal(0, overview.LowStockCount);
        Assert.Empty(overview.LowStockSupplies);
        Assert.Equal(0, overview.LentCount);
        Assert.Empty(overview.LentItems);
        Assert.Equal(0, overview.UnplacedCount);
        Assert.Empty(overview.UnplacedItems);
        Assert.Empty(overview.Areas);
    }

    [Fact]
    public async Task Overview_LowStock_CountsAtOrBelowThresholdOrderedByUrgency()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Pantry");

        var ample = await CreateSupplyAsync(client, "Ample batteries", "cells", threshold: 5m);
        await ReceiveAsync(client, ample.Id, areaId, 20m);

        var atThreshold = await CreateSupplyAsync(client, "Barely-low tape", "rolls", threshold: 5m);
        await ReceiveAsync(client, atThreshold.Id, areaId, 5m);

        // Never received — fully out of stock, the most urgent.
        var outOfStock = await CreateSupplyAsync(client, "Out-of-stock glue", "tubes", threshold: 5m);

        var noThreshold = await CreateSupplyAsync(client, "Untracked string", "spools");

        var archived = await CreateSupplyAsync(client, "Archived screws", "boxes", threshold: 5m);
        await PatchSupplyStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(2, overview.LowStockCount);
        Assert.Equal(2, overview.LowStockSupplies.Count);
        Assert.DoesNotContain(overview.LowStockSupplies, s => s.Id == ample.Id);
        Assert.DoesNotContain(overview.LowStockSupplies, s => s.Id == noThreshold.Id);
        Assert.DoesNotContain(overview.LowStockSupplies, s => s.Id == archived.Id);

        // Most urgent first: out-of-stock (ratio 0) before at-threshold (ratio 1).
        Assert.Equal(outOfStock.Id, overview.LowStockSupplies[0].Id);
        Assert.Equal(0m, overview.LowStockSupplies[0].TotalOnHand);
        Assert.Equal("tubes", overview.LowStockSupplies[0].Unit);
        Assert.Equal(5m, overview.LowStockSupplies[0].RestockThreshold);

        Assert.Equal(atThreshold.Id, overview.LowStockSupplies[1].Id);
        Assert.Equal(5m, overview.LowStockSupplies[1].TotalOnHand);
        Assert.Equal("rolls", overview.LowStockSupplies[1].Unit);
        Assert.Equal(5m, overview.LowStockSupplies[1].RestockThreshold);
    }

    [Fact]
    public async Task Overview_Lent_CountsLentWithLentToAndDropsOnReturn()
    {
        var client = await CreateAdminClientAsync();

        var ladder = await CreateItemAsync(client, new { name = "Ladder" });
        await PatchStatusAsync(client, ladder.Id, "Lent", lentTo: "Dave next door");

        var drill = await CreateItemAsync(client, new { name = "Drill" });
        await PatchStatusAsync(client, drill.Id, "Lent", lentTo: "Sam");
        await PatchStatusAsync(client, drill.Id, "Active"); // returned

        var sold = await CreateItemAsync(client, new { name = "Sold bike" });
        await PatchStatusAsync(client, sold.Id, "Sold");
        var discarded = await CreateItemAsync(client, new { name = "Discarded chair" });
        await PatchStatusAsync(client, discarded.Id, "Discarded");
        var archived = await CreateItemAsync(client, new { name = "Archived lamp" });
        await PatchStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(1, overview.LentCount);
        var preview = Assert.Single(overview.LentItems);
        Assert.Equal(ladder.Id, preview.Id);
        Assert.Equal("Ladder", preview.Name);
        Assert.Equal("Dave next door", preview.LentTo);
    }

    [Fact]
    public async Task Overview_Unplaced_UsesResolvedSemantics()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var noLocation = await CreateItemAsync(client, new { name = "Adrift wrench" });
        await CreateItemAsync(client, new { name = "Placed drill", areaId = garageId });

        // An item inside an unplaced container is resolved-unplaced — this distinguishes the
        // overview from GetItemsQuery.Unplaced's direct-placement semantics.
        var floatingBin = await CreateContainerAsync(client, "Floating bin");
        var nested = await CreateItemAsync(client, new { name = "Nested pliers", parentItemId = floatingBin.Id });

        var placedBin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);
        await CreateItemAsync(client, new { name = "Binned tape measure", parentItemId = placedBin.Id });

        var archivedAdrift = await CreateItemAsync(client, new { name = "Archived adrift" });
        await PatchStatusAsync(client, archivedAdrift.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        // The unplaced container itself counts too: noLocation + floatingBin + nested.
        Assert.Equal(3, overview.UnplacedCount);
        var ids = overview.UnplacedItems.Select(i => i.Id).ToList();
        Assert.Contains(noLocation.Id, ids);
        Assert.Contains(floatingBin.Id, ids);
        Assert.Contains(nested.Id, ids);
    }

    [Fact]
    public async Task Overview_Areas_CountsResolvedItems()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        // Direct item + placed container + item nested in it ⇒ 3 resolved to the garage.
        await CreateItemAsync(client, new { name = "Direct drill", areaId = garageId });
        var bin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);
        await CreateItemAsync(client, new { name = "Nested pliers", parentItemId = bin.Id });

        // Lent is live and counts; Sold is terminal and doesn't.
        var lent = await CreateItemAsync(client, new { name = "Lent ladder", areaId = garageId });
        await PatchStatusAsync(client, lent.Id, "Lent", lentTo: "Dave");
        var sold = await CreateItemAsync(client, new { name = "Sold bike", areaId = garageId });
        await PatchStatusAsync(client, sold.Id, "Sold");

        // An item in an unplaced container counts toward no area but is still unplaced.
        var floatingBin = await CreateContainerAsync(client, "Floating bin");
        await CreateItemAsync(client, new { name = "Adrift wrench", parentItemId = floatingBin.Id });

        var overview = await GetOverviewAsync(client);

        var garage = Assert.Single(overview.Areas);
        Assert.Equal("Garage", garage.Name);
        Assert.Equal(4, garage.ItemCount); // direct + bin + nested + lent
        Assert.Equal(2, overview.UnplacedCount); // floating bin + adrift wrench
    }

    [Fact]
    public async Task Overview_Areas_CountsDistinctSuppliesViaResolvedStock()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");

        var bin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);

        // Stock both directly in the garage and in a container resolving there ⇒ one type, counted
        // once for the garage; stock in the pantry counts the same type there too.
        var batteries = await CreateSupplyAsync(client, "Batteries", "cells");
        await ReceiveAsync(client, batteries.Id, garageId, 4m);
        await ReceiveAsync(client, batteries.Id, areaId: null, 6m, containerItemId: bin.Id);
        await ReceiveAsync(client, batteries.Id, pantryId, 2m);

        // Archived supplies keep their stock entries but are hidden from the list — excluded.
        var archived = await CreateSupplyAsync(client, "Archived screws", "boxes");
        await ReceiveAsync(client, archived.Id, garageId, 10m);
        await PatchSupplyStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(2, overview.Areas.Count);
        var garage = overview.Areas.Single(a => a.Id == garageId);
        Assert.Equal(2, garage.ItemCount); // the bin (a live item) + batteries once
        var pantry = overview.Areas.Single(a => a.Id == pantryId);
        Assert.Equal(1, pantry.ItemCount); // batteries
    }

    [Fact]
    public async Task Overview_Areas_IncludesZeroItemAreasOrderedByName()
    {
        var client = await CreateAdminClientAsync();
        var officeId = await CreateAreaAsync(client, "Office");
        var atticId = await CreateAreaAsync(client, "Attic");
        await CreateItemAsync(client, new { name = "Stapler", areaId = officeId });

        var overview = await GetOverviewAsync(client);

        Assert.Equal(2, overview.Areas.Count);
        Assert.Equal(atticId, overview.Areas[0].Id);
        Assert.Equal(0, overview.Areas[0].ItemCount);
        Assert.Equal(officeId, overview.Areas[1].Id);
        Assert.Equal(1, overview.Areas[1].ItemCount);
    }

    [Fact]
    public async Task Overview_PreviewsCappedAtFive()
    {
        var client = await CreateAdminClientAsync();

        for (var i = 1; i <= 7; i++)
        {
            var item = await CreateItemAsync(client, new { name = $"Lent thing {i}" });
            await PatchStatusAsync(client, item.Id, "Lent", lentTo: $"Friend {i}");
        }

        var overview = await GetOverviewAsync(client);

        Assert.Equal(7, overview.LentCount);
        Assert.Equal(5, overview.LentItems.Count);
    }

    [Fact]
    public async Task Overview_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var resp = await client.GetAsync("/api/v1/inventory/overview", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
