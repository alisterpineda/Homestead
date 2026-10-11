using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Brands;

public class GetBrandsHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetBrandsHandler _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetBrandsHandler(_db);
        await ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task AddBrandAsync(string name)
    {
        _db.Brands.Add(new Brand { Id = Guid.NewGuid(), Name = name });
        await _db.SaveChangesAsync(Ct);
    }

    private async Task<Brand> AddBrandWithItemsAsync(string name, int itemCount, DateTimeOffset? updatedAt = null)
    {
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
        };
        _db.Brands.Add(brand);
        for (var i = 0; i < itemCount; i++)
        {
            _db.Items.Add(new Item
            {
                Id = Guid.NewGuid(),
                Name = $"{name} item {i}",
                Fungibility = Fungibility.NonFungible,
                Status = ItemStatus.Active,
                BrandId = brand.Id,
            });
        }
        await _db.SaveChangesAsync(Ct);
        return brand;
    }

    [Fact]
    public async Task Handle_OrdersByName()
    {
        await AddBrandAsync("Sony");
        await AddBrandAsync("Apple");
        await AddBrandAsync("Makita");

        var resp = await _sut.HandleAsync(new GetBrandsQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(["Apple", "Makita", "Sony"], resp.Items.Select(b => b.Name));
    }

    [Fact]
    public async Task Handle_FiltersBySearch_CaseInsensitive()
    {
        await AddBrandAsync("Apple");
        await AddBrandAsync("Samsung");

        var resp = await _sut.HandleAsync(new GetBrandsQuery { Page = 1, PageSize = 50, Search = "APP" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Apple", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_Paginates_TotalCountReflectsWholeSet()
    {
        for (var i = 0; i < 5; i++)
            await AddBrandAsync($"Brand {i:D2}");

        var resp = await _sut.HandleAsync(new GetBrandsQuery { Page = 1, PageSize = 2 }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Equal(5, resp.TotalCount);
    }

    [Fact]
    public async Task Handle_PopulatesItemCount()
    {
        await AddBrandWithItemsAsync("Apple", itemCount: 2);
        await AddBrandWithItemsAsync("Sony", itemCount: 0);

        var resp = await _sut.HandleAsync(new GetBrandsQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(2, resp.Items.Single(b => b.Name == "Apple").ItemCount);
        Assert.Equal(0, resp.Items.Single(b => b.Name == "Sony").ItemCount);
    }

    [Fact]
    public async Task Handle_SortByItems_Descending()
    {
        await AddBrandWithItemsAsync("Apple", itemCount: 1);
        await AddBrandWithItemsAsync("Sony", itemCount: 3);
        await AddBrandWithItemsAsync("Makita", itemCount: 2);

        var resp = await _sut.HandleAsync(
            new GetBrandsQuery { Page = 1, PageSize = 50, SortBy = "items", SortDir = "desc" }, Ct);

        Assert.Equal(["Sony", "Makita", "Apple"], resp.Items.Select(b => b.Name));
    }

    [Fact]
    public async Task Handle_SortByUpdated_Ascending()
    {
        var now = DateTimeOffset.UtcNow;
        await AddBrandWithItemsAsync("Newest", itemCount: 0, updatedAt: now);
        await AddBrandWithItemsAsync("Oldest", itemCount: 0, updatedAt: now.AddDays(-2));
        await AddBrandWithItemsAsync("Middle", itemCount: 0, updatedAt: now.AddDays(-1));

        var resp = await _sut.HandleAsync(
            new GetBrandsQuery { Page = 1, PageSize = 50, SortBy = "updated", SortDir = "asc" }, Ct);

        Assert.Equal(["Oldest", "Middle", "Newest"], resp.Items.Select(b => b.Name));
    }

    [Fact]
    public async Task Handle_SortByName_Descending()
    {
        await AddBrandAsync("Apple");
        await AddBrandAsync("Sony");
        await AddBrandAsync("Makita");

        var resp = await _sut.HandleAsync(
            new GetBrandsQuery { Page = 1, PageSize = 50, SortBy = "name", SortDir = "desc" }, Ct);

        Assert.Equal(["Sony", "Makita", "Apple"], resp.Items.Select(b => b.Name));
    }
}
