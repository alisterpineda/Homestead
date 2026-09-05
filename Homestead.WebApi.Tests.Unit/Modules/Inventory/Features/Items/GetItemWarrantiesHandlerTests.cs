using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.GetWarranties;
using Microsoft.Extensions.Time.Testing;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class GetItemWarrantiesHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db;
    private readonly GetItemWarrantiesHandler _sut;

    public GetItemWarrantiesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetItemWarrantiesHandler(_db, new FakeTimeProvider(Now));
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingItem_ReturnsNull()
    {
        Assert.Null(await _sut.Handle(new GetItemWarrantiesQuery(Guid.NewGuid()), Ct));
    }

    [Fact]
    public async Task Handle_ItemWithNoWarranties_ReturnsEmptyList()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var result = await _sut.Handle(new GetItemWarrantiesQuery(item.Id), Ct);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_OrdersByExpiryAscending_NullsLast()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var later = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, expiresOn: new DateOnly(2029, 1, 1));
        var openEnded = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id);
        var sooner = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, expiresOn: new DateOnly(2027, 1, 1));

        var result = await _sut.Handle(new GetItemWarrantiesQuery(item.Id), Ct);

        Assert.NotNull(result);
        Assert.Equal([sooner.Id, later.Id, openEnded.Id], result.Select(w => w.Id));
    }

    [Fact]
    public async Task Handle_TiesBreakOnCreatedAtThenId()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var sameDay = new DateOnly(2027, 6, 1);
        var second = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            expiresOn: sameDay, createdAt: Now.AddHours(-1));
        var first = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            expiresOn: sameDay, createdAt: Now.AddHours(-2));

        var result = await _sut.Handle(new GetItemWarrantiesQuery(item.Id), Ct);

        Assert.NotNull(result);
        Assert.Equal([first.Id, second.Id], result.Select(w => w.Id));
    }

    [Fact]
    public async Task Handle_ProjectsPerRowStatus()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, expiresOn: new DateOnly(2026, 2, 28));
        await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, expiresOn: new DateOnly(2026, 3, 31));
        await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, expiresOn: new DateOnly(2026, 4, 1));
        await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id);

        var result = await _sut.Handle(new GetItemWarrantiesQuery(item.Id), Ct);

        Assert.NotNull(result);
        // Per-row, not per-item: each row carries its own verdict, so a lapsed tier stays visibly
        // lapsed even while the item as a whole is still covered.
        Assert.Equal(
            [WarrantyStatus.Expired, WarrantyStatus.Expiring, WarrantyStatus.Active, WarrantyStatus.Active],
            result.Select(w => w.Status));
    }

    [Fact]
    public async Task Handle_ScopesToTheItem()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var mine = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, provider: "Bosch");
        await ItemWarrantyTestData.AddWarrantyAsync(_db, other.Id, provider: "Miele");

        var result = await _sut.Handle(new GetItemWarrantiesQuery(item.Id), Ct);

        Assert.NotNull(result);
        var only = Assert.Single(result);
        Assert.Equal(mine.Id, only.Id);
        Assert.Equal("Bosch", only.Provider);
    }

    [Fact]
    public async Task Handle_OnConsumable_ReturnsEmptyRatherThan404()
    {
        // The route is on /items, so a consumable id is a legitimate lookup — it just has no rows.
        var consumable = await ItemNoteTestData.AddItemAsync(_db, Fungibility.Fungible);

        var result = await _sut.Handle(new GetItemWarrantiesQuery(consumable.Id), Ct);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
