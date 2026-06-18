using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record OverviewResult(
    int LowStockCount, IReadOnlyList<LowStockPreviewItem> LowStockConsumables,
    int LentCount, IReadOnlyList<LentPreviewItem> LentDurables,
    int UnplacedCount, IReadOnlyList<UnplacedPreviewItem> UnplacedDurables,
    IReadOnlyList<AreaSummaryItem> Areas,
    IReadOnlyList<RecentlyUpdatedPreview> RecentlyUpdated);

internal record LowStockPreviewItem(
    Guid Id, string Name, decimal TotalOnHand, string? Unit, decimal RestockThreshold);

internal record LentPreviewItem(Guid Id, string Name, string? LentTo);

internal record UnplacedPreviewItem(Guid Id, string Name);

internal record AreaSummaryItem(Guid Id, string Name, int ItemCount, int LowStockCount);

// Exactly one of Durable / Consumable is populated per entry (the other deserializes to null).
internal record RecentlyUpdatedPreview(
    DateTimeOffset LastActivityAt, DurableItem? Durable, ConsumableItem? Consumable);

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

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static Task<DurableItem> CreateContainerAsync(
        HttpClient client, string name, Guid? areaId = null, Guid? parentItemId = null) =>
        CreateDurableAsync(client, new { name, areaId, parentItemId, isContainer = true });

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static async Task<ConsumableItem> CreateConsumableAsync(
        HttpClient client, string name, string unit = "units", decimal? threshold = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name, unit, restockThreshold = threshold }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    private static async Task ReceiveAsync(
        HttpClient client, Guid consumableId, Guid? areaId, decimal quantity, Guid? containerItemId = null)
    {
        var resp = await client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/receive",
            new { areaId, containerItemId, quantity }, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task PatchStatusAsync(
        HttpClient client, Guid itemId, string status, string? lentTo = null)
    {
        var ops = new List<object> { new { op = "replace", path = "/status", value = (object)status } };
        if (lentTo is not null)
            ops.Add(new { op = "replace", path = "/lentTo", value = (object)lentTo });
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{itemId}", ops.ToArray());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private static async Task PatchConsumableStatusAsync(HttpClient client, Guid consumableId, string status)
    {
        var resp = await SendJsonPatchAsync(client, $"/api/v1/inventory/consumables/{consumableId}",
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
        Assert.Empty(overview.LowStockConsumables);
        Assert.Equal(0, overview.LentCount);
        Assert.Empty(overview.LentDurables);
        Assert.Equal(0, overview.UnplacedCount);
        Assert.Empty(overview.UnplacedDurables);
        Assert.Empty(overview.Areas);
    }

    [Fact]
    public async Task Overview_LowStock_CountsAtOrBelowThresholdOrderedByUrgency()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Pantry");

        var ample = await CreateConsumableAsync(client, "Ample batteries", "cells", threshold: 5m);
        await ReceiveAsync(client, ample.Id, areaId, 20m);

        var atThreshold = await CreateConsumableAsync(client, "Barely-low tape", "rolls", threshold: 5m);
        await ReceiveAsync(client, atThreshold.Id, areaId, 5m);

        // Never received — fully out of stock, the most urgent.
        var outOfStock = await CreateConsumableAsync(client, "Out-of-stock glue", "tubes", threshold: 5m);

        var noThreshold = await CreateConsumableAsync(client, "Untracked string", "spools");

        var archived = await CreateConsumableAsync(client, "Archived screws", "boxes", threshold: 5m);
        await PatchConsumableStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(2, overview.LowStockCount);
        Assert.Equal(2, overview.LowStockConsumables.Count);
        Assert.DoesNotContain(overview.LowStockConsumables, s => s.Id == ample.Id);
        Assert.DoesNotContain(overview.LowStockConsumables, s => s.Id == noThreshold.Id);
        Assert.DoesNotContain(overview.LowStockConsumables, s => s.Id == archived.Id);

        // Most urgent first: out-of-stock (ratio 0) before at-threshold (ratio 1).
        Assert.Equal(outOfStock.Id, overview.LowStockConsumables[0].Id);
        Assert.Equal(0m, overview.LowStockConsumables[0].TotalOnHand);
        Assert.Equal("tubes", overview.LowStockConsumables[0].Unit);
        Assert.Equal(5m, overview.LowStockConsumables[0].RestockThreshold);

        Assert.Equal(atThreshold.Id, overview.LowStockConsumables[1].Id);
        Assert.Equal(5m, overview.LowStockConsumables[1].TotalOnHand);
        Assert.Equal("rolls", overview.LowStockConsumables[1].Unit);
        Assert.Equal(5m, overview.LowStockConsumables[1].RestockThreshold);
    }

    [Fact]
    public async Task Overview_Lent_CountsLentWithLentToAndDropsOnReturn()
    {
        var client = await CreateAdminClientAsync();

        var ladder = await CreateDurableAsync(client, new { name = "Ladder" });
        await PatchStatusAsync(client, ladder.Id, "Lent", lentTo: "Dave next door");

        var drill = await CreateDurableAsync(client, new { name = "Drill" });
        await PatchStatusAsync(client, drill.Id, "Lent", lentTo: "Sam");
        await PatchStatusAsync(client, drill.Id, "Active"); // returned

        var sold = await CreateDurableAsync(client, new { name = "Sold bike" });
        await PatchStatusAsync(client, sold.Id, "Sold");
        var discarded = await CreateDurableAsync(client, new { name = "Discarded chair" });
        await PatchStatusAsync(client, discarded.Id, "Discarded");
        var archived = await CreateDurableAsync(client, new { name = "Archived lamp" });
        await PatchStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(1, overview.LentCount);
        var preview = Assert.Single(overview.LentDurables);
        Assert.Equal(ladder.Id, preview.Id);
        Assert.Equal("Ladder", preview.Name);
        Assert.Equal("Dave next door", preview.LentTo);
    }

    [Fact]
    public async Task Overview_Unplaced_UsesResolvedSemantics()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var noLocation = await CreateDurableAsync(client, new { name = "Adrift wrench" });
        await CreateDurableAsync(client, new { name = "Placed drill", areaId = garageId });

        // An item inside an unplaced container is resolved-unplaced — this distinguishes the
        // overview from GetDurablesQuery.Unplaced's direct-placement semantics.
        var floatingBin = await CreateContainerAsync(client, "Floating bin");
        var nested = await CreateDurableAsync(client, new { name = "Nested pliers", parentItemId = floatingBin.Id });

        var placedBin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);
        await CreateDurableAsync(client, new { name = "Binned tape measure", parentItemId = placedBin.Id });

        var archivedAdrift = await CreateDurableAsync(client, new { name = "Archived adrift" });
        await PatchStatusAsync(client, archivedAdrift.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        // The unplaced container itself counts too: noLocation + floatingBin + nested.
        Assert.Equal(3, overview.UnplacedCount);
        var ids = overview.UnplacedDurables.Select(i => i.Id).ToList();
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
        await CreateDurableAsync(client, new { name = "Direct drill", areaId = garageId });
        var bin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);
        await CreateDurableAsync(client, new { name = "Nested pliers", parentItemId = bin.Id });

        // Lent is live and counts; Sold is terminal and doesn't.
        var lent = await CreateDurableAsync(client, new { name = "Lent ladder", areaId = garageId });
        await PatchStatusAsync(client, lent.Id, "Lent", lentTo: "Dave");
        var sold = await CreateDurableAsync(client, new { name = "Sold bike", areaId = garageId });
        await PatchStatusAsync(client, sold.Id, "Sold");

        // An item in an unplaced container counts toward no area but is still unplaced.
        var floatingBin = await CreateContainerAsync(client, "Floating bin");
        await CreateDurableAsync(client, new { name = "Adrift wrench", parentItemId = floatingBin.Id });

        var overview = await GetOverviewAsync(client);

        var garage = Assert.Single(overview.Areas);
        Assert.Equal("Garage", garage.Name);
        Assert.Equal(4, garage.ItemCount); // direct + bin + nested + lent
        Assert.Equal(2, overview.UnplacedCount); // floating bin + adrift wrench
    }

    [Fact]
    public async Task Overview_Areas_CountsDistinctConsumablesViaResolvedStock()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");

        var bin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);

        // Stock both directly in the garage and in a container resolving there ⇒ one type, counted
        // once for the garage; stock in the pantry counts the same type there too.
        var batteries = await CreateConsumableAsync(client, "Batteries", "cells");
        await ReceiveAsync(client, batteries.Id, garageId, 4m);
        await ReceiveAsync(client, batteries.Id, areaId: null, 6m, containerItemId: bin.Id);
        await ReceiveAsync(client, batteries.Id, pantryId, 2m);

        // Archived consumables keep their stock entries but are hidden from the list — excluded.
        var archived = await CreateConsumableAsync(client, "Archived screws", "boxes");
        await ReceiveAsync(client, archived.Id, garageId, 10m);
        await PatchConsumableStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        Assert.Equal(2, overview.Areas.Count);
        var garage = overview.Areas.Single(a => a.Id == garageId);
        Assert.Equal(2, garage.ItemCount); // the bin (a live item) + batteries once
        var pantry = overview.Areas.Single(a => a.Id == pantryId);
        Assert.Equal(1, pantry.ItemCount); // batteries
    }

    [Fact]
    public async Task Overview_Areas_CarryResolvedLowStockCounts()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var pantryId = await CreateAreaAsync(client, "Pantry");

        var bin = await CreateContainerAsync(client, "Garage bin", areaId: garageId);

        // Low stock inside a container resolves to the container's area.
        var lowGlue = await CreateConsumableAsync(client, "Glue", "tubes", threshold: 5m);
        await ReceiveAsync(client, lowGlue.Id, areaId: null, 2m, containerItemId: bin.Id);

        // Ample stock and archived-low stock count toward no area's low badge.
        var ampleTape = await CreateConsumableAsync(client, "Tape", "rolls", threshold: 5m);
        await ReceiveAsync(client, ampleTape.Id, pantryId, 50m);
        var archived = await CreateConsumableAsync(client, "Archived screws", "boxes", threshold: 5m);
        await ReceiveAsync(client, archived.Id, pantryId, 1m);
        await PatchConsumableStatusAsync(client, archived.Id, "Archived");

        var overview = await GetOverviewAsync(client);

        var garage = overview.Areas.Single(a => a.Id == garageId);
        Assert.Equal(1, garage.LowStockCount);
        var pantry = overview.Areas.Single(a => a.Id == pantryId);
        Assert.Equal(0, pantry.LowStockCount);
    }

    [Fact]
    public async Task Overview_Areas_IncludesZeroItemAreasOrderedByName()
    {
        var client = await CreateAdminClientAsync();
        var officeId = await CreateAreaAsync(client, "Office");
        var atticId = await CreateAreaAsync(client, "Attic");
        await CreateDurableAsync(client, new { name = "Stapler", areaId = officeId });

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
            var item = await CreateDurableAsync(client, new { name = $"Lent thing {i}" });
            await PatchStatusAsync(client, item.Id, "Lent", lentTo: $"Friend {i}");
        }

        var overview = await GetOverviewAsync(client);

        Assert.Equal(7, overview.LentCount);
        Assert.Equal(5, overview.LentDurables.Count);
    }

    [Fact]
    public async Task Overview_RecentlyUpdated_StockActivitySurfacesConsumableOrderedAndCappedAtSix()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");

        // Create the consumable FIRST so its metadata (Item.UpdatedAt) is the oldest activity in the
        // household. If recency keyed off Item.UpdatedAt it would rank last and the cap would drop it.
        var screws = await CreateConsumableAsync(client, "Wood screws", "pcs");

        // Then eight durables, each newer than the consumable's metadata. The newest is placed in the
        // garage so we can prove the API enriches a durable's resolved location for the quick panel.
        var durables = new List<Guid>();
        for (var i = 1; i <= 7; i++)
            durables.Add((await CreateDurableAsync(client, new { name = $"Durable {i}" })).Id);
        var placed = await CreateDurableAsync(client, new { name = "Placed drill", areaId });
        durables.Add(placed.Id);

        // Finally restock the consumable — its stock-entry timestamp is now the most recent activity
        // household-wide, so effective-recency must float it to the front of the strip.
        await ReceiveAsync(client, screws.Id, areaId, 25m);

        var overview = await GetOverviewAsync(client);

        // Capped at six (one consumable + eight durables = nine live items).
        Assert.Equal(6, overview.RecentlyUpdated.Count);
        Assert.All(overview.RecentlyUpdated, r => Assert.True((r.Durable is null) ^ (r.Consumable is null)));

        // Stock activity wins: the restocked consumable leads, ahead of every more-recently-created durable.
        var first = overview.RecentlyUpdated[0];
        Assert.NotNull(first.Consumable);
        Assert.Equal(screws.Id, first.Consumable!.Id);

        // The most-recently-touched durable comes next, location-enriched (no re-fetch for the panel).
        var second = overview.RecentlyUpdated[1];
        Assert.NotNull(second.Durable);
        Assert.Equal(placed.Id, second.Durable!.Id);
        Assert.Equal(areaId, second.Durable!.ResolvedAreaId);
        Assert.NotNull(second.Durable!.Path);
        Assert.NotEmpty(second.Durable!.Path!);

        // The two oldest durables fall outside the six-item cap.
        var ids = overview.RecentlyUpdated.Select(r => r.Durable?.Id).ToList();
        Assert.DoesNotContain(durables[0], ids);
        Assert.DoesNotContain(durables[1], ids);
    }

    [Fact]
    public async Task Overview_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var resp = await client.GetAsync("/api/v1/inventory/overview", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
