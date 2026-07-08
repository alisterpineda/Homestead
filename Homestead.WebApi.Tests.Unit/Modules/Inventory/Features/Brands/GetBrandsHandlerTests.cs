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

    [Fact]
    public async Task Handle_OrdersByName()
    {
        await AddBrandAsync("Sony");
        await AddBrandAsync("Apple");
        await AddBrandAsync("Makita");

        var resp = await _sut.Handle(new GetBrandsQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(["Apple", "Makita", "Sony"], resp.Items.Select(b => b.Name));
    }

    [Fact]
    public async Task Handle_FiltersBySearch_CaseInsensitive()
    {
        await AddBrandAsync("Apple");
        await AddBrandAsync("Samsung");

        var resp = await _sut.Handle(new GetBrandsQuery { Page = 1, PageSize = 50, Search = "APP" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Apple", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_Paginates_TotalCountReflectsWholeSet()
    {
        for (var i = 0; i < 5; i++)
            await AddBrandAsync($"Brand {i:D2}");

        var resp = await _sut.Handle(new GetBrandsQuery { Page = 1, PageSize = 2 }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Equal(5, resp.TotalCount);
    }
}
