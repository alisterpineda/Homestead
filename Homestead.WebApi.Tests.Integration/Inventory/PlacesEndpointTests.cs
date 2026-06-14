using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record PlaceContentsResult(
    IReadOnlyList<LocationSegmentItem> Path, ItemItem? ContainerItem,
    int DirectItemCount, int NestedItemCount,
    int ScopedLowStockCount, int ScopedLentCount,
    IReadOnlyList<PlaceContainerItem> Containers,
    IReadOnlyList<ItemItem> Items,
    IReadOnlyList<PlaceSupplyItem> Supplies);

internal record PlaceContainerItem(
    Guid Id, string Name, string Status, int ItemCount, int LowStockCount,
    IReadOnlyList<string> PreviewNames);

internal record PlaceSupplyItem(SupplyItem Supply, decimal LocalQuantity);

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

    private static async Task<ItemItem> CreateItemAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
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

    // ----- Tests -----

    [Fact]
    public async Task Contents_AreaScope_ReturnsDirectContentsAndCounts()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var chest = await CreateItemAsync(client, new { name = "Chest", areaId = garageId, isContainer = true });
        var drill = await CreateItemAsync(client, new { name = "Drill", areaId = garageId });
        await CreateItemAsync(client, new { name = "Nested wrench", parentItemId = chest.Id });

        var lowGlue = await CreateSupplyAsync(client, "Glue", "tubes", threshold: 5m);
        await ReceiveAsync(client, lowGlue.Id, areaId: null, 2m, containerItemId: chest.Id);
        var tape = await CreateSupplyAsync(client, "Tape", "rolls");
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
        var supplyRow = Assert.Single(contents.Supplies);
        Assert.Equal(tape.Id, supplyRow.Supply.Id);
        Assert.Equal(3m, supplyRow.LocalQuantity);

        Assert.Equal(3, contents.DirectItemCount); // chest + drill + tape
        Assert.Equal(2, contents.NestedItemCount); // wrench + glue
    }

    [Fact]
    public async Task Contents_AreaScope_PopulatesScopedLowStockAndLentCounts()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        // A low supply stored in the garage.
        var glue = await CreateSupplyAsync(client, "Glue", "tubes", threshold: 5m);
        await ReceiveAsync(client, glue.Id, garageId, 2m);

        // An item lent from the garage.
        var ladder = await CreateItemAsync(client, new { name = "Ladder", areaId = garageId });
        var lendResp = await SendJsonPatchAsync(client, $"/api/v1/inventory/items/{ladder.Id}",
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
        var chest = await CreateItemAsync(client, new { name = "Chest", areaId = garageId, isContainer = true });
        var bin = await CreateItemAsync(client, new { name = "Bin", parentItemId = chest.Id, isContainer = true });

        var screws = await CreateSupplyAsync(client, "Screws", "screws");
        await ReceiveAsync(client, screws.Id, areaId: null, 10m, containerItemId: bin.Id);
        await ReceiveAsync(client, screws.Id, garageId, 5m);

        var contents = await GetContentsAsync(client, $"containerItemId={bin.Id}");

        Assert.Equal(new[] { "Garage", "Chest", "Bin" }, contents.Path.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { "Area", "Container", "Container" }, contents.Path.Select(s => s.Kind).ToArray());
        Assert.Equal(bin.Id, contents.ContainerItem!.Id);
        Assert.Equal(garageId, contents.ContainerItem.ResolvedAreaId);

        var supplyRow = Assert.Single(contents.Supplies);
        Assert.Equal(10m, supplyRow.LocalQuantity);
        Assert.Equal(15m, supplyRow.Supply.TotalOnHand);
        Assert.Equal(2, supplyRow.Supply.StockLocations!.Count);
    }

    [Fact]
    public async Task Contents_UnplacedScope_DirectlyUnplacedOnly()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");

        var floatingBin = await CreateItemAsync(client, new { name = "Floating bin", isContainer = true });
        var adrift = await CreateItemAsync(client, new { name = "Adrift wrench" });
        await CreateItemAsync(client, new { name = "Nested pliers", parentItemId = floatingBin.Id });
        await CreateItemAsync(client, new { name = "Placed drill", areaId = garageId });

        var contents = await GetContentsAsync(client, "unplaced=true");

        Assert.Empty(contents.Path);
        Assert.Null(contents.ContainerItem);
        Assert.Equal(floatingBin.Id, Assert.Single(contents.Containers).Id);
        Assert.Equal(adrift.Id, Assert.Single(contents.Items).Id);
        Assert.Empty(contents.Supplies);
        Assert.Equal(2, contents.DirectItemCount);
        Assert.Equal(1, contents.NestedItemCount); // the nested pliers
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
