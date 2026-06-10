using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

[Collection("Integration")]
public class ItemsEndpointTests : IntegrationTestBase
{
    public ItemsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<ItemItem> CreateItemAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var area = await resp.Content.ReadFromJsonAsync<AreaItem>(CT);
        return area!.Id;
    }

    [Fact]
    public async Task CreateItem_AppliesNonFungibleActiveDefaults()
    {
        var client = await CreateAdminClientAsync();

        var item = await CreateItemAsync(client, new { name = "Cordless drill" });

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
        var item = await CreateItemAsync(client, new { name = "Drill" });

        // Move into an area.
        var moveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/areaId", value = (object)garageId } });
        Assert.Equal(HttpStatusCode.OK, moveResp.StatusCode);
        var moved = await moveResp.Content.ReadFromJsonAsync<ItemItem>(CT);
        Assert.Equal(garageId, moved!.AreaId);
        Assert.Equal("Garage", moved.AreaName);

        // Unplace.
        var unplaceResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/areaId", value = (object?)null } });
        Assert.Equal(HttpStatusCode.OK, unplaceResp.StatusCode);
        var unplaced = await unplaceResp.Content.ReadFromJsonAsync<ItemItem>(CT);
        Assert.Null(unplaced!.AreaId);
        Assert.Null(unplaced.AreaName);
    }

    [Fact]
    public async Task PatchItem_LentRoundTrip_SetsThenAutoClearsLentTo()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateItemAsync(client, new { name = "Ladder" });

        var lendResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new object[]
            {
                new { op = "replace", path = "/status", value = (object)"Lent" },
                new { op = "replace", path = "/lentTo", value = (object)"Dave next door" },
            });
        Assert.Equal(HttpStatusCode.OK, lendResp.StatusCode);
        var lent = await lendResp.Content.ReadFromJsonAsync<ItemItem>(CT);
        Assert.Equal("Lent", lent!.Status);
        Assert.Equal("Dave next door", lent.LentTo);

        // Leaving Lent auto-clears the LentTo note.
        var returnResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Active" } });
        Assert.Equal(HttpStatusCode.OK, returnResp.StatusCode);
        var returned = await returnResp.Content.ReadFromJsonAsync<ItemItem>(CT);
        Assert.Equal("Active", returned!.Status);
        Assert.Null(returned.LentTo);
    }

    [Fact]
    public async Task PatchItem_LentWithoutLentTo_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateItemAsync(client, new { name = "Ladder" });

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Lent" } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetItems_ExcludesTerminalUnlessIncludeInactive()
    {
        var client = await CreateAdminClientAsync();
        var live = await CreateItemAsync(client, new { name = "Live drill" });
        var archived = await CreateItemAsync(client, new { name = "Old drill" });

        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{archived.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var defaultList = await (await client.GetAsync("/api/v1/inventory/items?page=1&pageSize=200", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ItemItem>>(CT);
        Assert.Contains(defaultList!.Items, i => i.Id == live.Id);
        Assert.DoesNotContain(defaultList.Items, i => i.Id == archived.Id);

        var inactiveList = await (await client.GetAsync(
                "/api/v1/inventory/items?page=1&pageSize=200&includeInactive=true", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<ItemItem>>(CT);
        Assert.Contains(inactiveList!.Items, i => i.Id == archived.Id);
    }

    [Fact]
    public async Task DeleteItem_CleanItem_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var item = await CreateItemAsync(client, new { name = "Disposable" });

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/items/{item.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }
}
