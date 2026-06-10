using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record AreaItem(Guid Id, string Name, int LiveItemCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal record ItemItem(
    Guid Id, string Name, string Fungibility, string Status,
    Guid? AreaId, string? AreaName, string? LentTo, string? Serial, bool IsContainer);

[Collection("Integration")]
public class AreasEndpointTests : IntegrationTestBase
{
    public AreasEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<AreaItem> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!;
    }

    private static async Task<ItemItem> CreateItemAsync(HttpClient client, string name, Guid? areaId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/items", new { name, areaId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ItemItem>(CT))!;
    }

    [Fact]
    public async Task CreateGetPatchDelete_RoundTrips()
    {
        var client = await CreateAdminClientAsync();

        var created = await CreateAreaAsync(client, "Garage");
        Assert.Equal("Garage", created.Name);
        Assert.Equal(0, created.LiveItemCount);

        var list = await (await client.GetAsync("/api/v1/inventory/areas?page=1&pageSize=100", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<AreaItem>>(CT);
        Assert.Contains(list!.Items, a => a.Id == created.Id && a.Name == "Garage");

        var patchResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/areas/{created.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Workshop" } });
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<AreaItem>(CT);
        Assert.Equal("Workshop", patched!.Name);

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/areas/{created.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task GetAreas_LiveItemCount_CountsDirectlyPlacedLiveItems()
    {
        var client = await CreateAdminClientAsync();
        var area = await CreateAreaAsync(client, "Pantry");
        await CreateItemAsync(client, "Flour bin", area.Id);
        await CreateItemAsync(client, "Sugar bin", area.Id);

        var list = await (await client.GetAsync("/api/v1/inventory/areas?page=1&pageSize=100", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<AreaItem>>(CT);

        var found = list!.Items.Single(a => a.Id == area.Id);
        Assert.Equal(2, found.LiveItemCount);
    }

    [Fact]
    public async Task DeleteArea_WithLiveItem_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var area = await CreateAreaAsync(client, "Garage");
        await CreateItemAsync(client, "Drill", area.Id);

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/areas/{area.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteArea_SucceedsAfterArchivingItem_AndClearsItemAreaId()
    {
        var client = await CreateAdminClientAsync();
        var area = await CreateAreaAsync(client, "Garage");
        var item = await CreateItemAsync(client, "Old drill", area.Id);

        // Archiving (terminal) removes it from the live-item block.
        var archiveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/items/{item.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });
        Assert.Equal(HttpStatusCode.OK, archiveResp.StatusCode);

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/areas/{area.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // FK SetNull clears the (now-stale) location label on the surviving archived item.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Items.AsNoTracking().SingleAsync(i => i.Id == item.Id, CT);
        Assert.Null(stored.AreaId);
        Assert.Equal(ItemStatus.Archived, stored.Status);
    }
}
