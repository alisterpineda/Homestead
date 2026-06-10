using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Areas;

public class GetAreasHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetAreasHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetAreasHandler(_db);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_LiveItemCount_ExcludesTerminalAndUnplaced()
    {
        var garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(garage);

        // Two live in the garage, one terminal in the garage, one live elsewhere (unplaced).
        _db.Items.AddRange(
            Item(garage.Id, ItemStatus.Active),
            Item(garage.Id, ItemStatus.Lent),
            Item(garage.Id, ItemStatus.Sold),
            Item(null, ItemStatus.Active));
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        var area = Assert.Single(resp.Items);
        Assert.Equal("Garage", area.Name);
        Assert.Equal(2, area.LiveItemCount);
    }

    [Fact]
    public async Task Handle_OrdersByName()
    {
        _db.Areas.AddRange(
            new Area { Id = Guid.NewGuid(), Name = "Office" },
            new Area { Id = Guid.NewGuid(), Name = "Attic" });
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(["Attic", "Office"], resp.Items.Select(a => a.Name));
    }

    private static Item Item(Guid? areaId, ItemStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Thing",
        Fungibility = Fungibility.NonFungible,
        Status = status,
        AreaId = areaId,
    };
}
