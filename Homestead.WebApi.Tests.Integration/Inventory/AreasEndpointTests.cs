using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record AreaItem(
    Guid Id, string Name, Guid? ParentAreaId, string? ParentAreaName,
    int LiveItemCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal record ChildAreaItem(Guid Id, string Name, int ItemCount, int LowStockCount);
internal record PlaceContentsLite(
    IReadOnlyList<LocationSegmentItem> Path, IReadOnlyList<ChildAreaItem> ChildAreas);
internal record DurableItem(
    Guid Id, string Name, string Fungibility, string Status,
    Guid? AreaId, string? AreaName, string? LentTo, string? Serial, bool IsContainer,
    Guid? ParentItemId, string? Compartment, Guid? ResolvedAreaId,
    IReadOnlyList<LocationSegmentItem>? Path);
// Enum serializes as a string server-side, so Kind is "Area" | "Container".
internal record LocationSegmentItem(Guid Id, string Name, string Kind);

[Collection("Integration")]
public class AreasEndpointTests : IntegrationTestBase
{
    public AreasEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<AreaItem> CreateAreaAsync(HttpClient client, string name, Guid? parentAreaId = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name, parentAreaId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!;
    }

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, string name, Guid? areaId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name, areaId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
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
        await CreateDurableAsync(client, "Flour bin", area.Id);
        await CreateDurableAsync(client, "Sugar bin", area.Id);

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
        await CreateDurableAsync(client, "Drill", area.Id);

        var deleteResp = await client.DeleteAsync($"/api/v1/inventory/areas/{area.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteArea_SucceedsAfterArchivingItem_AndClearsItemAreaId()
    {
        var client = await CreateAdminClientAsync();
        var area = await CreateAreaAsync(client, "Garage");
        var item = await CreateDurableAsync(client, "Old drill", area.Id);

        // Archiving (terminal) removes it from the live-item block.
        var archiveResp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{item.Id}",
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

    // ----- Nested areas -----

    [Fact]
    public async Task CreateNested_ExposesParentFields()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        var basement = await CreateAreaAsync(client, "Basement", house.Id);

        Assert.Equal(house.Id, basement.ParentAreaId);
        Assert.Equal("House", basement.ParentAreaName);
    }

    [Fact]
    public async Task CreateNested_ParentNotFound_Returns400()
    {
        // Consistent with PatchArea reparent: a bad parent reference is a 400 business-rule error.
        var client = await CreateAdminClientAsync();
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas",
            new { name = "Orphan", parentAreaId = Guid.NewGuid() }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchReparent_MovesAreaAndResolvesNewParentName()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        var basement = await CreateAreaAsync(client, "Basement");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/areas/{basement.Id}",
            new[] { new { op = "replace", path = "/parentAreaId", value = (object)house.Id } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var patched = await resp.Content.ReadFromJsonAsync<AreaItem>(CT);
        Assert.Equal(house.Id, patched!.ParentAreaId);
        Assert.Equal("House", patched.ParentAreaName);
    }

    [Fact]
    public async Task PatchReparent_UnderOwnDescendant_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        var basement = await CreateAreaAsync(client, "Basement", house.Id);
        var bedroom = await CreateAreaAsync(client, "Bedroom", basement.Id);

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/areas/{house.Id}",
            new[] { new { op = "replace", path = "/parentAreaId", value = (object)bedroom.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchReparent_ParentNotFound_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var basement = await CreateAreaAsync(client, "Basement");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/areas/{basement.Id}",
            new[] { new { op = "replace", path = "/parentAreaId", value = (object)Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteArea_WithSubAreas_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        await CreateAreaAsync(client, "Basement", house.Id);

        var resp = await client.DeleteAsync($"/api/v1/inventory/areas/{house.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetAreas_LiveItemCount_RollsUpThroughNestedAreas()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        var basement = await CreateAreaAsync(client, "Basement", house.Id);
        await CreateDurableAsync(client, "Boiler", basement.Id);

        var list = await (await client.GetAsync("/api/v1/inventory/areas?page=1&pageSize=100", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<AreaItem>>(CT);

        Assert.Equal(1, list!.Items.Single(a => a.Id == basement.Id).LiveItemCount);
        Assert.Equal(1, list.Items.Single(a => a.Id == house.Id).LiveItemCount); // rolled up
    }

    [Fact]
    public async Task GetPlaceContents_Area_ReturnsChildAreasAndFullBreadcrumb()
    {
        var client = await CreateAdminClientAsync();
        var house = await CreateAreaAsync(client, "House");
        var basement = await CreateAreaAsync(client, "Basement", house.Id);
        var bedroom = await CreateAreaAsync(client, "Bedroom", basement.Id);
        await CreateDurableAsync(client, "Lamp", bedroom.Id);

        // Drilling Basement: breadcrumb shows the full ancestor path, Bedroom is a child-area card
        // carrying its rolled count (the lamp).
        var basementContents = await (await client.GetAsync(
            $"/api/v1/inventory/places/contents?areaId={basement.Id}", CT))
            .Content.ReadFromJsonAsync<PlaceContentsLite>(CT);

        Assert.Equal(new[] { "House", "Basement" }, basementContents!.Path.Select(s => s.Name).ToArray());
        var childCard = Assert.Single(basementContents.ChildAreas);
        Assert.Equal(bedroom.Id, childCard.Id);
        Assert.Equal(1, childCard.ItemCount);

        // Deepest drill: the breadcrumb carries every ancestor.
        var bedroomContents = await (await client.GetAsync(
            $"/api/v1/inventory/places/contents?areaId={bedroom.Id}", CT))
            .Content.ReadFromJsonAsync<PlaceContentsLite>(CT);
        Assert.Equal(new[] { "House", "Basement", "Bedroom" }, bedroomContents!.Path.Select(s => s.Name).ToArray());
        Assert.Empty(bedroomContents.ChildAreas);
    }
}
