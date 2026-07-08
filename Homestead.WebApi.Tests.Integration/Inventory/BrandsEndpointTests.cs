using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Inventory;

internal record BrandItem(Guid Id, string Name);
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
