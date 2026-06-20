using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record PlaceContentsResult(
    IReadOnlyList<LocationSegmentItem> Path, DurableItem? ContainerItem,
    int ScopedLowStockCount, int ScopedLentCount,
    IReadOnlyList<PlaceContainerItem> Containers,
    IReadOnlyList<DurableItem> Items,
    IReadOnlyList<PlaceConsumableItem> Consumables);

internal record PlaceContainerItem(
    Guid Id, string Name, string Status, int ItemCount, int LowStockCount,
    IReadOnlyList<string> PreviewNames);

internal record PlaceConsumableItem(ConsumableItem Consumable, decimal LocalQuantity);

[Collection("Integration")]
public class PlacesEndpointTests : IntegrationTestBase
{
    public PlacesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<PlaceContentsResult> GetContentsAsync(HttpClient client, string query)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/places/contents?{query}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PlaceContentsResult>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
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

    // ----- Tests -----

    [Fact]
    public async Task Contents_AreaScope_ReturnsDirectContents()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var chest = await CreateDurableAsync(client, new { name = "Chest", areaId = garageId, isContainer = true });
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });
        await CreateDurableAsync(client, new { name = "Nested wrench", parentItemId = chest.Id });

        var lowGlue = await CreateConsumableAsync(client, "Glue", "tubes", threshold: 5m);
        await ReceiveAsync(client, lowGlue.Id, areaId: null, 2m, containerItemId: chest.Id);
        var tape = await CreateConsumableAsync(client, "Tape", "rolls");
        await ReceiveAsync(client, tape.Id, garageId, 3m);

        var contents = await GetContentsAsync(client, $"areaId={garageId}");

        var segment = Assert.Single(contents.Path);
        Assert.Equal("Garage", segment.Name);
        Assert.Equal("Area", segment.Kind);
        Assert.Null(contents.ContainerItem);

        var card = Assert.Single(contents.Containers);
        Assert.Equal(chest.Id, card.Id);
        Assert.Equal(2, card.ItemCount); // wrench + glue
        Assert.Equal(1, card.LowStockCount);
        Assert.Equal(new[] { "Glue", "Nested wrench" }, card.PreviewNames);

        Assert.Equal(drill.Id, Assert.Single(contents.Items).Id);
        var consumableRow = Assert.Single(contents.Consumables);
        Assert.Equal(tape.Id, consumableRow.Consumable.Id);
        Assert.Equal(3m, consumableRow.LocalQuantity);
    }

    [Fact]
    public async Task Contents_AreaScope_PopulatesScopedLowStockAndLentCounts()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        // A low consumable stored in the garage.
        var glue = await CreateConsumableAsync(client, "Glue", "tubes", threshold: 5m);
        await ReceiveAsync(client, glue.Id, garageId, 2m);

        // An item lent from the garage.
        var ladder = await CreateDurableAsync(client, new { name = "Ladder", areaId = garageId });
        var lendResp = await SendJsonPatchAsync(client, $"/api/v1/inventory/durables/{ladder.Id}",
            new object[]
            {
                new { op = "replace", path = "/status", value = (object)"Lent" },
                new { op = "replace", path = "/lentTo", value = (object)"Dave next door" },
            });
        Assert.Equal(HttpStatusCode.OK, lendResp.StatusCode);

        var contents = await GetContentsAsync(client, $"areaId={garageId}");

        Assert.Equal(1, contents.ScopedLowStockCount);
        Assert.Equal(1, contents.ScopedLentCount);
    }

    [Fact]
    public async Task Contents_ContainerScope_BreadcrumbAndLocalQuantities()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateDurableAsync(client, new { name = "Chest", areaId = garageId, isContainer = true });
        var bin = await CreateDurableAsync(client, new { name = "Bin", parentItemId = chest.Id, isContainer = true });

        var screws = await CreateConsumableAsync(client, "Screws", "screws");
        await ReceiveAsync(client, screws.Id, areaId: null, 10m, containerItemId: bin.Id);
        await ReceiveAsync(client, screws.Id, garageId, 5m);

        var contents = await GetContentsAsync(client, $"containerItemId={bin.Id}");

        Assert.Equal(new[] { "Garage", "Chest", "Bin" }, contents.Path.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { "Area", "Container", "Container" }, contents.Path.Select(s => s.Kind).ToArray());
        Assert.Equal(bin.Id, contents.ContainerItem!.Id);
        Assert.Equal(garageId, contents.ContainerItem.ResolvedAreaId);

        var consumableRow = Assert.Single(contents.Consumables);
        Assert.Equal(10m, consumableRow.LocalQuantity);
        Assert.Equal(15m, consumableRow.Consumable.TotalOnHand);
        Assert.Equal(2, consumableRow.Consumable.StockLocations!.Count);
    }

    [Fact]
    public async Task Contents_UnplacedScope_DirectlyUnplacedOnly()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var floatingBin = await CreateDurableAsync(client, new { name = "Floating bin", isContainer = true });
        var adrift = await CreateDurableAsync(client, new { name = "Adrift wrench" });
        await CreateDurableAsync(client, new { name = "Nested pliers", parentItemId = floatingBin.Id });
        await CreateDurableAsync(client, new { name = "Placed drill", areaId = garageId });

        var contents = await GetContentsAsync(client, "unplaced=true");

        Assert.Empty(contents.Path);
        Assert.Null(contents.ContainerItem);
        Assert.Equal(floatingBin.Id, Assert.Single(contents.Containers).Id);
        Assert.Equal(adrift.Id, Assert.Single(contents.Items).Id);
        Assert.Empty(contents.Consumables);
    }

    [Fact]
    public async Task Contents_NoScope_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync("/api/v1/inventory/places/contents", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Contents_MissingContainer_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync(
            $"/api/v1/inventory/places/contents?containerItemId={Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Contents_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var resp = await client.GetAsync("/api/v1/inventory/places/contents?unplaced=true", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
