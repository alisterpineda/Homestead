using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// The unified detail envelope returned by GET /inventory/items/{id}. Exactly one of Durable /
// Consumable is set, matching the Fungibility discriminator — letting the frontend dispatcher land
// on the route without knowing the kind up front.
internal record ItemDetailItem(Guid Id, string Fungibility, DurableItem? Durable, ConsumableDetailItem? Consumable);

[Collection("Integration")]
public class ItemsEndpointTests : IntegrationTestBase
{
    public ItemsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static async Task<ConsumableItem> CreateConsumableAsync(HttpClient client, string name, string unit)
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

    [Fact]
    public async Task GetItem_DurableId_ReturnsDurableShape()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var drill = await CreateDurableAsync(client, new { name = "Cordless drill", areaId = garageId });

        var resp = await client.GetAsync($"/api/v1/inventory/items/{drill.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var item = (await resp.Content.ReadFromJsonAsync<ItemDetailItem>(CT))!;
        Assert.Equal(drill.Id, item.Id);
        Assert.Equal("NonFungible", item.Fungibility);
        Assert.Null(item.Consumable);
        Assert.NotNull(item.Durable);
        Assert.Equal("Cordless drill", item.Durable!.Name);
        // The location resolver ran (AreaName/ResolvedAreaId are filled post-query, not by the projection).
        Assert.Equal(garageId, item.Durable.ResolvedAreaId);
        Assert.Equal("Garage", item.Durable.AreaName);
    }

    [Fact]
    public async Task GetItem_ConsumableId_ReturnsConsumableShape()
    {
        var client = await CreateAdminClientAsync();
        var screws = await CreateConsumableAsync(client, "Wood screws", "screws");

        var resp = await client.GetAsync($"/api/v1/inventory/items/{screws.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var item = (await resp.Content.ReadFromJsonAsync<ItemDetailItem>(CT))!;
        Assert.Equal(screws.Id, item.Id);
        Assert.Equal("Fungible", item.Fungibility);
        Assert.Null(item.Durable);
        Assert.NotNull(item.Consumable);
        Assert.Equal("Wood screws", item.Consumable!.Consumable.Name);
        Assert.Equal("screws", item.Consumable.Consumable.Unit);
        Assert.NotNull(item.Consumable.Locations); // empty (no stock received yet), but present.
    }

    [Fact]
    public async Task GetItem_UnknownId_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync($"/api/v1/inventory/items/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetItem_DurableId_ReturnsTags()
    {
        var client = await CreateAdminClientAsync();
        var drill = await CreateDurableAsync(client, new { name = "Cordless drill", tags = new[] { "Tools/Cordless" } });

        var resp = await client.GetAsync($"/api/v1/inventory/items/{drill.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var item = (await resp.Content.ReadFromJsonAsync<ItemDetailItem>(CT))!;
        Assert.NotNull(item.Durable);
        var badge = Assert.Single(item.Durable!.Tags!);
        Assert.Equal("Cordless", badge.Name);
        Assert.Equal("Tools/Cordless", badge.Path);
    }

    [Fact]
    public async Task GetItem_ConsumableId_ReturnsTags()
    {
        var client = await CreateAdminClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Wood screws", unit = "screws", tags = new[] { "Tools/Fasteners" } }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var screws = (await createResp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;

        var resp = await client.GetAsync($"/api/v1/inventory/items/{screws.Id}", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var item = (await resp.Content.ReadFromJsonAsync<ItemDetailItem>(CT))!;
        Assert.NotNull(item.Consumable);
        var badge = Assert.Single(item.Consumable!.Consumable.Tags!);
        Assert.Equal("Fasteners", badge.Name);
        Assert.Equal("Tools/Fasteners", badge.Path);
    }
}
