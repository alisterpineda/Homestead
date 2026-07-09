using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record BrandItem(Guid Id, string Name);
internal record BrandDetail(Guid Id, string Name, int ItemCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
internal record BrandDurableItem(Guid Id, string Name, Guid? BrandId, string? BrandName, string? Model);
internal record BrandConsumableItem(Guid Id, string Name, Guid? BrandId, string? BrandName);
internal record BrandItemDetail(Guid Id, string Fungibility, BrandDurableItem? Durable);

[Collection("Integration")]
public class BrandsEndpointTests : IntegrationTestBase
{
    public BrandsEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private const string CheckViolation = "23514";

    private static async Task<BrandItem> CreateBrandAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/brands", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<BrandItem>(CT))!;
    }

    private static async Task<BrandDurableItem> CreateDurableWithBrandAsync(
        HttpClient client, string name, Guid brandId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name, brandId }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<BrandDurableItem>(CT))!;
    }

    // ----- PatchBrand (rename) -----

    [Fact]
    public async Task PatchBrand_Rename_Returns200AndUpdatesRow()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/brands/{apple.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Apple Inc" } });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var patched = await resp.Content.ReadFromJsonAsync<BrandDetail>(CT);
        Assert.Equal("Apple Inc", patched!.Name);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Apple Inc", (await db.Brands.AsNoTracking().SingleAsync(b => b.Id == apple.Id, CT)).Name);
    }

    [Fact]
    public async Task PatchBrand_RenameOntoExistingName_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateBrandAsync(client, "Apple");
        var samsung = await CreateBrandAsync(client, "Samsung");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/brands/{samsung.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"apple" } });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PatchBrand_BlankName_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/brands/{apple.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"   " } });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PatchBrand_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await SendJsonPatchAsync(client,
            $"/api/v1/inventory/brands/{Guid.NewGuid()}",
            new[] { new { op = "replace", path = "/name", value = (object)"Apple" } });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PatchBrand_NonContributor_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(admin, "Apple");
        var viewer = await CreateViewerClientAsync();

        var resp = await SendJsonPatchAsync(viewer,
            $"/api/v1/inventory/brands/{apple.Id}",
            new[] { new { op = "replace", path = "/name", value = (object)"Apple Inc" } });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ----- DeleteBrand -----

    [Fact]
    public async Task DeleteBrand_Unused_Returns204()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var resp = await client.DeleteAsync($"/api/v1/inventory/brands/{apple.Id}", CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Brands.AsNoTracking().AnyAsync(b => b.Id == apple.Id, CT));
    }

    [Fact]
    public async Task DeleteBrand_InUse_Returns400AndKeepsRow()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");
        await CreateDurableWithBrandAsync(client, "MacBook", apple.Id);

        var resp = await client.DeleteAsync($"/api/v1/inventory/brands/{apple.Id}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Brands.AsNoTracking().AnyAsync(b => b.Id == apple.Id, CT));
    }

    [Fact]
    public async Task DeleteBrand_Missing_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.DeleteAsync($"/api/v1/inventory/brands/{Guid.NewGuid()}", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- MergeBrand -----

    [Fact]
    public async Task MergeBrand_RepointsItems_RemovesSource_GrowsTargetCount()
    {
        var client = await CreateAdminClientAsync();
        var source = await CreateBrandAsync(client, "Apple Inc");
        var target = await CreateBrandAsync(client, "Apple");
        var sourceItem = await CreateDurableWithBrandAsync(client, "iPhone", source.Id);
        await CreateDurableWithBrandAsync(client, "iPad", target.Id);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inventory/brands/{source.Id}/merge", new { targetId = target.Id }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var merged = await resp.Content.ReadFromJsonAsync<BrandDetail>(CT);
        Assert.Equal(target.Id, merged!.Id);
        Assert.Equal(2, merged.ItemCount);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Brands.AsNoTracking().AnyAsync(b => b.Id == source.Id, CT));
        Assert.Equal(target.Id,
            (await db.Items.AsNoTracking().SingleAsync(i => i.Id == sourceItem.Id, CT)).BrandId);
    }

    [Fact]
    public async Task MergeBrand_Self_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inventory/brands/{apple.Id}/merge", new { targetId = apple.Id }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MergeBrand_MissingTarget_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var source = await CreateBrandAsync(client, "Apple Inc");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inventory/brands/{source.Id}/merge", new { targetId = Guid.NewGuid() }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task MergeBrand_MissingSource_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var target = await CreateBrandAsync(client, "Apple");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inventory/brands/{Guid.NewGuid()}/merge", new { targetId = target.Id }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task MergeBrand_NonContributor_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var source = await CreateBrandAsync(admin, "Apple Inc");
        var target = await CreateBrandAsync(admin, "Apple");
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(
            $"/api/v1/inventory/brands/{source.Id}/merge", new { targetId = target.Id }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateBrand_DuplicateCaseVariant_Returns201AndSameId()
    {
        var client = await CreateAdminClientAsync();

        var first = await CreateBrandAsync(client, "Apple");

        // Find-or-create: a case-variant name returns the same row (201, same Id) — the unique
        // lower(Name) index holds and dedup happens in the handler.
        var dupResp = await client.PostAsJsonAsync("/api/v1/inventory/brands", new { name = "APPLE" }, CT);
        Assert.Equal(HttpStatusCode.Created, dupResp.StatusCode);
        var dup = (await dupResp.Content.ReadFromJsonAsync<BrandItem>(CT))!;

        Assert.Equal(first.Id, dup.Id);

        var list = await (await client.GetAsync("/api/v1/inventory/brands?page=1&pageSize=100&search=apple", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<BrandItem>>(CT);
        Assert.Single(list!.Items, b => b.Id == first.Id);
    }

    [Fact]
    public async Task CreateDurable_WithBrandAndModel_SurfacesOnItemDetail()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var createResp = await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "MacBook Pro", brandId = apple.Id, model = "MRW23" }, CT);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var durable = (await createResp.Content.ReadFromJsonAsync<BrandDurableItem>(CT))!;
        Assert.Equal(apple.Id, durable.BrandId);
        Assert.Equal("Apple", durable.BrandName);
        Assert.Equal("MRW23", durable.Model);

        var detail = (await (await client.GetAsync($"/api/v1/inventory/items/{durable.Id}", CT))
            .Content.ReadFromJsonAsync<BrandItemDetail>(CT))!;
        Assert.NotNull(detail.Durable);
        Assert.Equal(apple.Id, detail.Durable!.BrandId);
        Assert.Equal("Apple", detail.Durable.BrandName);
        Assert.Equal("MRW23", detail.Durable.Model);
    }

    [Fact]
    public async Task GetDurables_FilterByBrand_NarrowsToBrand()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");
        var sony = await CreateBrandAsync(client, "Sony");

        var mac = (await (await client.PostAsJsonAsync("/api/v1/inventory/durables",
            new { name = "MacBook", brandId = apple.Id }, CT)).Content.ReadFromJsonAsync<BrandDurableItem>(CT))!;
        await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name = "Bravia", brandId = sony.Id }, CT);

        var list = await (await client.GetAsync(
                $"/api/v1/inventory/durables?page=1&pageSize=100&brandId={apple.Id}", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<BrandDurableItem>>(CT);

        Assert.Single(list!.Items);
        Assert.Equal(mac.Id, list.Items[0].Id);
        Assert.Equal("Apple", list.Items[0].BrandName);
    }

    [Fact]
    public async Task GetConsumables_FilterByBrand_NarrowsToBrand()
    {
        var client = await CreateAdminClientAsync();
        var apple = await CreateBrandAsync(client, "Apple");

        var cable = (await (await client.PostAsJsonAsync("/api/v1/inventory/consumables",
                new { name = "Lightning cables", unit = "pcs", brandId = apple.Id }, CT))
            .Content.ReadFromJsonAsync<BrandConsumableItem>(CT))!;
        await client.PostAsJsonAsync("/api/v1/inventory/consumables",
            new { name = "Generic wire", unit = "m" }, CT);

        var list = await (await client.GetAsync(
                $"/api/v1/inventory/consumables?page=1&pageSize=100&brandId={apple.Id}", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<BrandConsumableItem>>(CT);

        Assert.Single(list!.Items);
        Assert.Equal(cable.Id, list.Items[0].Id);
        Assert.Equal("Apple", list.Items[0].BrandName);
    }

    // ----- GetBrands: enrichment + sort translation (real Postgres; InMemory unit tests can't prove
    // the correlated-count ORDER BY / projection translates) -----

    [Fact]
    public async Task GetBrands_SortByItemsDesc_OrdersByCountThenName_AndPopulatesItemCount()
    {
        var client = await CreateAdminClientAsync();
        var alpha = await CreateBrandAsync(client, "Alpha");   // 2 items
        await CreateBrandAsync(client, "Bravo");               // 0 items
        var charlie = await CreateBrandAsync(client, "Charlie"); // 1 item
        await CreateDurableWithBrandAsync(client, "A1", alpha.Id);
        await CreateDurableWithBrandAsync(client, "A2", alpha.Id);
        await CreateDurableWithBrandAsync(client, "C1", charlie.Id);

        var list = await (await client.GetAsync(
                "/api/v1/inventory/brands?page=1&pageSize=100&sortBy=items&sortDir=desc", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<BrandDetail>>(CT);

        Assert.Equal(["Alpha", "Charlie", "Bravo"], list!.Items.Select(b => b.Name));
        Assert.Equal([2, 1, 0], list.Items.Select(b => b.ItemCount));
    }

    [Fact]
    public async Task GetBrands_SortByItems_TiesBreakByName()
    {
        // All three tie on itemCount 0 → the Name tiebreaker gives a deterministic order (else
        // pagination could skip/repeat rows). Insertion order differs from name order on purpose.
        var client = await CreateAdminClientAsync();
        await CreateBrandAsync(client, "Zeta");
        await CreateBrandAsync(client, "Alpha");
        await CreateBrandAsync(client, "Mike");

        var list = await (await client.GetAsync(
                "/api/v1/inventory/brands?page=1&pageSize=100&sortBy=items&sortDir=asc", CT))
            .Content.ReadFromJsonAsync<PaginatedResult<BrandDetail>>(CT);

        Assert.Equal(["Alpha", "Mike", "Zeta"], list!.Items.Select(b => b.Name));
    }

    [Fact]
    public async Task GetBrands_SortByUpdated_Translates()
    {
        var client = await CreateAdminClientAsync();
        await CreateBrandAsync(client, "Apple");
        await CreateBrandAsync(client, "Sony");

        var resp = await client.GetAsync(
            "/api/v1/inventory/brands?page=1&pageSize=100&sortBy=updated&sortDir=desc", CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<PaginatedResult<BrandDetail>>(CT);
        Assert.Equal(2, list!.Items.Count);
    }

    [Fact]
    public async Task Constraint_RejectsFungibleRowWithNonNullModel()
    {
        // Model is durable-only: the rewritten CK_items_nonfungible_fields check now includes
        // "Model" IS NULL in the fungible branch. Exercise it directly, bypassing handler validation.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"Unit\", \"Model\") "
            + "VALUES (gen_random_uuid(), 'Bad consumable', 'Fungible', 'Active', 'units', 'MRW23')", CT));

        Assert.Equal(CheckViolation, ex.SqlState);
    }
}
