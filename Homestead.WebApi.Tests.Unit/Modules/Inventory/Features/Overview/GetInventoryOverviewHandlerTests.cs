using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Overview;

public class GetInventoryOverviewHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetInventoryOverviewHandler _sut = null!;
    private Area _garage = null!;

    // A fixed base time so per-row UpdatedAt offsets give a deterministic recency order. The
    // InMemory provider doesn't apply the CURRENT_TIMESTAMP default, so every row sets it explicitly.
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(_garage);
        await _db.SaveChangesAsync();
        _sut = new GetInventoryOverviewHandler(
            _db, new InventoryPlacementService(_db), new ItemLocationResolver(_db));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddDurableAsync(
        string name, DateTimeOffset updatedAt, ItemStatus status = ItemStatus.Active, Guid? areaId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private async Task<Item> AddConsumableAsync(
        string name, DateTimeOffset itemUpdatedAt, decimal? quantity = null,
        DateTimeOffset? stockUpdatedAt = null, decimal? restockThreshold = null,
        ItemStatus status = ItemStatus.Active)
    {
        var consumable = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.Fungible,
            Status = status,
            Unit = "pcs",
            RestockThreshold = restockThreshold,
            CreatedAt = itemUpdatedAt,
            UpdatedAt = itemUpdatedAt,
        };
        _db.Items.Add(consumable);
        if (quantity is { } qty)
        {
            _db.StockEntries.Add(new StockEntry
            {
                Id = Guid.NewGuid(),
                ItemId = consumable.Id,
                AreaId = _garage.Id,
                Quantity = qty,
                CreatedAt = stockUpdatedAt ?? itemUpdatedAt,
                UpdatedAt = stockUpdatedAt ?? itemUpdatedAt,
            });
        }
        await _db.SaveChangesAsync(Ct);
        return consumable;
    }

    private Task<GetInventoryOverviewResponse> HandleAsync() =>
        _sut.Handle(new GetInventoryOverviewQuery(), Ct);

    [Fact]
    public async Task RecentlyUpdated_OrdersByEffectiveActivityNewestFirst()
    {
        var older = await AddDurableAsync("Older drill", T0.AddHours(1));
        var newest = await AddDurableAsync("Newest saw", T0.AddHours(3));
        var middle = await AddDurableAsync("Middle wrench", T0.AddHours(2));

        var resp = await HandleAsync();

        Assert.Equal(
            new[] { newest.Id, middle.Id, older.Id },
            resp.RecentlyUpdated.Select(r => r.Durable!.Id));
        Assert.All(resp.RecentlyUpdated, r =>
        {
            Assert.NotNull(r.Durable);
            Assert.Null(r.Consumable);
        });
    }

    [Fact]
    public async Task RecentlyUpdated_StockActivityOutranksMoreRecentlyEditedItem()
    {
        // The consumable's metadata is stale (T0+1h) but it was restocked at T0+5h — its effective
        // last-activity is the stock time, which must beat the durable edited at T0+3h.
        var restocked = await AddConsumableAsync(
            "Restocked screws", itemUpdatedAt: T0.AddHours(1), quantity: 10m, stockUpdatedAt: T0.AddHours(5));
        var edited = await AddDurableAsync("Freshly edited drill", T0.AddHours(3));

        var resp = await HandleAsync();

        Assert.Equal(2, resp.RecentlyUpdated.Count);
        Assert.Equal(restocked.Id, resp.RecentlyUpdated[0].Consumable!.Id);
        Assert.Equal(T0.AddHours(5), resp.RecentlyUpdated[0].LastActivityAt);
        Assert.Equal(edited.Id, resp.RecentlyUpdated[1].Durable!.Id);
        Assert.Equal(T0.AddHours(3), resp.RecentlyUpdated[1].LastActivityAt);
    }

    [Fact]
    public async Task RecentlyUpdated_CapsAtTen()
    {
        // Twelve live durables, ascending recency; only the ten most recent survive the cap.
        var ids = new List<Guid>();
        for (var i = 1; i <= 12; i++)
            ids.Add((await AddDurableAsync($"Item {i}", T0.AddHours(i))).Id);

        var resp = await HandleAsync();

        Assert.Equal(10, resp.RecentlyUpdated.Count);
        var returned = resp.RecentlyUpdated.Select(r => r.Durable!.Id).ToList();
        // The ten newest (hours 12..3), newest first; the two oldest (hours 1, 2) are dropped.
        Assert.Equal(ids.AsEnumerable().Reverse().Take(10), returned);
        Assert.DoesNotContain(ids[0], returned);
        Assert.DoesNotContain(ids[1], returned);
    }

    [Fact]
    public async Task RecentlyUpdated_MixesTypesByEffectiveRecencyAcrossTheCap()
    {
        // A consumable whose metadata edit (T0+9h) is newer than its last restock (T0+2h): effective
        // recency must be the item edit, exercising the ternary's false branch (Item beats stock).
        var editedConsumable = await AddConsumableAsync(
            "Edited consumable", itemUpdatedAt: T0.AddHours(9), quantity: 5m, stockUpdatedAt: T0.AddHours(2));
        // A consumable touched only by a restock (metadata stale): effective = stock time (T0+7h).
        var restocked = await AddConsumableAsync(
            "Restocked consumable", itemUpdatedAt: T0.AddHours(1), quantity: 3m, stockUpdatedAt: T0.AddHours(7));
        // Ten durables spanning the ranking, including two that must fall outside the ten-item cap.
        var d11 = await AddDurableAsync("Durable h11", T0.AddHours(11));
        var d10 = await AddDurableAsync("Durable h10", T0.AddHours(10));
        var d8 = await AddDurableAsync("Durable h8", T0.AddHours(8));
        var d6 = await AddDurableAsync("Durable h6", T0.AddHours(6));
        var d5 = await AddDurableAsync("Durable h5", T0.AddHours(5));
        var d4 = await AddDurableAsync("Durable h4", T0.AddHours(4));
        var d3 = await AddDurableAsync("Durable h3", T0.AddHours(3));
        var d2 = await AddDurableAsync("Durable h2", T0.AddHours(2));
        var d1 = await AddDurableAsync("Durable h1", T0.AddHours(1));
        var d0 = await AddDurableAsync("Durable h0", T0);

        var resp = await HandleAsync();

        // Effective order interleaves both types: d11(11), d10(10), editedConsumable(9), d8(8),
        // restocked(7), d6(6), d5(5), d4(4), d3(3), d2(2) | dropped: d1(1), d0(0).
        Assert.Equal(10, resp.RecentlyUpdated.Count);
        var orderedIds = resp.RecentlyUpdated.Select(r => r.Durable?.Id ?? r.Consumable!.Id).ToList();
        Assert.Equal(
            new[] { d11.Id, d10.Id, editedConsumable.Id, d8.Id, restocked.Id, d6.Id, d5.Id, d4.Id, d3.Id, d2.Id },
            orderedIds);
        // The two oldest durables fall outside the cap even though one outranks no consumable.
        Assert.DoesNotContain(d1.Id, orderedIds);
        Assert.DoesNotContain(d0.Id, orderedIds);
        // The edited consumable ranks by its metadata time, not its stale stock time.
        Assert.Equal(editedConsumable.Id, resp.RecentlyUpdated[2].Consumable!.Id);
        Assert.Equal(T0.AddHours(9), resp.RecentlyUpdated[2].LastActivityAt);
    }

    [Fact]
    public async Task RecentlyUpdated_ExcludesTerminalStatusItems()
    {
        var live = await AddDurableAsync("Live drill", T0.AddHours(1));
        // Terminal items, edited more recently — their bumped UpdatedAt must not surface them.
        await AddDurableAsync("Archived box", T0.AddHours(5), ItemStatus.Archived);
        await AddDurableAsync("Sold bike", T0.AddHours(6), ItemStatus.Sold);
        await AddDurableAsync("Discarded TV", T0.AddHours(7), ItemStatus.Discarded);
        await AddConsumableAsync("Archived caulk", T0.AddHours(8), status: ItemStatus.Archived);

        var resp = await HandleAsync();

        var entry = Assert.Single(resp.RecentlyUpdated);
        Assert.Equal(live.Id, entry.Durable!.Id);
    }

    [Fact]
    public async Task RecentlyUpdated_DurablesArriveLocationEnriched()
    {
        await AddDurableAsync("Placed drill", T0.AddHours(1), areaId: _garage.Id);

        var resp = await HandleAsync();

        var durable = Assert.Single(resp.RecentlyUpdated).Durable;
        Assert.NotNull(durable);
        // EnrichAsync filled the resolved path so the durable quick panel needs no re-fetch.
        Assert.Equal(_garage.Id, durable.ResolvedAreaId);
        Assert.NotEmpty(durable.Path);
        Assert.Equal("Garage", durable.Path[^1].Name);
    }

    [Fact]
    public async Task RecentlyUpdated_ConsumablesArriveLocationEnriched()
    {
        // AddConsumableAsync places the stock in _garage, so the recency row must arrive with its
        // resolved stock location attached — the row renders its location text with no re-fetch.
        await AddConsumableAsync("Placed screws", T0.AddHours(1), quantity: 10m);

        var resp = await HandleAsync();

        var consumable = Assert.Single(resp.RecentlyUpdated).Consumable;
        Assert.NotNull(consumable);
        // ConsumableStockLocationLoader filled the per-location breakdown, resolved to the garage.
        var location = Assert.Single(consumable.StockLocations);
        Assert.Equal(_garage.Id, location.AreaId);
        Assert.NotEmpty(location.Path);
        Assert.Equal("Garage", location.Path[^1].Name);
    }

    [Fact]
    public async Task RecentlyUpdated_EmptyWhenNothingLive()
    {
        var resp = await HandleAsync();
        Assert.Empty(resp.RecentlyUpdated);
    }
}
