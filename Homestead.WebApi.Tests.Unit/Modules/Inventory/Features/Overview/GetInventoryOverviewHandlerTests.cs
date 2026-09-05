using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;
using Homestead.WebApi.Modules.Inventory.Services;
using Microsoft.Extensions.Time.Testing;

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
            _db, new InventoryPlacementService(_db), new ItemLocationResolver(_db),
            new FakeTimeProvider(T0));
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
            var stockAt = stockUpdatedAt ?? itemUpdatedAt;
            _db.StockEntries.Add(new StockEntry
            {
                Id = Guid.NewGuid(),
                ItemId = consumable.Id,
                AreaId = _garage.Id,
                Quantity = qty,
                CreatedAt = stockAt,
                UpdatedAt = stockAt,
            });
            // Recency now derives from the append-only movement ledger (B5) — a consumed-to-zero item
            // keeps its bump even after its entry is purged — so the "stock touched at" instant must be
            // expressed as a movement's CreatedAt, not just the (purge-prone) entry's UpdatedAt.
            _db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                ItemId = consumable.Id,
                Kind = MovementKind.Flow,
                Quantity = qty,
                ToAreaId = _garage.Id,
                OccurredAt = stockAt,
                CreatedAt = stockAt,
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

    // ----- Warranty expiring tile -----

    // "Today" is T0 = 2026-01-01 (the FakeTimeProvider above), so the inclusive window edge is
    // 2026-01-31 and 2026-02-01 is one day too far out.
    private static readonly DateOnly Today = new(2026, 1, 1);

    private async Task AddWarrantyAsync(Guid itemId, DateOnly? expiresOn)
    {
        _db.ItemWarranties.Add(new ItemWarranty
        {
            Id = Guid.NewGuid(),
            ItemId = itemId,
            ExpiresOn = expiresOn,
            CreatedAt = T0,
            UpdatedAt = T0,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task ExpiringWarranties_CountsAndPreviewsMostUrgentFirst()
    {
        var soon = await AddDurableAsync("Dryer", T0);
        await AddWarrantyAsync(soon.Id, Today.AddDays(3));
        var later = await AddDurableAsync("Washer", T0);
        await AddWarrantyAsync(later.Id, Today.AddDays(20));

        var resp = await HandleAsync();

        Assert.Equal(2, resp.ExpiringWarrantyCount);
        Assert.Equal([soon.Id, later.Id], resp.ExpiringWarranties.Select(w => w.Id));
        Assert.Equal(["Dryer", "Washer"], resp.ExpiringWarranties.Select(w => w.Name));
        Assert.Equal(Today.AddDays(3), resp.ExpiringWarranties[0].ExpiresOn);
    }

    [Fact]
    public async Task ExpiringWarranties_LatestExpiryDecides_AndIsWhatThePreviewShows()
    {
        // A lapsed narrow tier alongside cover that runs out next week: the item is expiring on the
        // later date, and that later date is the one the tile shows.
        var item = await AddDurableAsync("Dishwasher", T0);
        await AddWarrantyAsync(item.Id, Today.AddDays(-100));
        await AddWarrantyAsync(item.Id, Today.AddDays(7));

        var resp = await HandleAsync();

        Assert.Equal(1, resp.ExpiringWarrantyCount);
        Assert.Equal(Today.AddDays(7), Assert.Single(resp.ExpiringWarranties).ExpiresOn);
    }

    [Fact]
    public async Task ExpiringWarranties_OpenEndedRowKeepsAnItemOut()
    {
        // A lifetime warranty means the item is covered for good, whatever the other rows say —
        // the same guard DurableResponseMapper applies.
        var item = await AddDurableAsync("Cast iron pan", T0);
        await AddWarrantyAsync(item.Id, Today.AddDays(5));
        await AddWarrantyAsync(item.Id, null);

        var resp = await HandleAsync();

        Assert.Equal(0, resp.ExpiringWarrantyCount);
        Assert.Empty(resp.ExpiringWarranties);
    }

    [Fact]
    public async Task ExpiringWarranties_ExcludesExpiredAndFarFutureAndNoWarranty()
    {
        var expired = await AddDurableAsync("Old kettle", T0);
        await AddWarrantyAsync(expired.Id, Today.AddDays(-1));
        var far = await AddDurableAsync("New fridge", T0);
        await AddWarrantyAsync(far.Id, Today.AddDays(31));
        await AddDurableAsync("Hammer", T0);

        var resp = await HandleAsync();

        Assert.Equal(0, resp.ExpiringWarrantyCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task ExpiringWarranties_IncludesBothWindowEdges(int days)
    {
        var item = await AddDurableAsync("Edge case", T0);
        await AddWarrantyAsync(item.Id, Today.AddDays(days));

        var resp = await HandleAsync();

        Assert.Equal(1, resp.ExpiringWarrantyCount);
    }

    [Theory]
    [InlineData(ItemStatus.Archived)]
    [InlineData(ItemStatus.Sold)]
    [InlineData(ItemStatus.Discarded)]
    public async Task ExpiringWarranties_ExcludesTerminalStatuses(ItemStatus status)
    {
        var item = await AddDurableAsync("Sold dryer", T0, status: status);
        await AddWarrantyAsync(item.Id, Today.AddDays(5));

        var resp = await HandleAsync();

        Assert.Equal(0, resp.ExpiringWarrantyCount);
    }

    [Theory]
    [InlineData(ItemStatus.Lent)]
    [InlineData(ItemStatus.InRepair)]
    public async Task ExpiringWarranties_IncludesTheOtherLiveStatuses(ItemStatus status)
    {
        var item = await AddDurableAsync("Lent drill", T0, status: status);
        await AddWarrantyAsync(item.Id, Today.AddDays(5));

        var resp = await HandleAsync();

        Assert.Equal(1, resp.ExpiringWarrantyCount);
    }

    [Fact]
    public async Task ExpiringWarranties_PreviewCapsAtFive()
    {
        for (var i = 1; i <= 7; i++)
        {
            var item = await AddDurableAsync($"Item {i:00}", T0);
            await AddWarrantyAsync(item.Id, Today.AddDays(i));
        }

        var resp = await HandleAsync();

        Assert.Equal(7, resp.ExpiringWarrantyCount);
        Assert.Equal(5, resp.ExpiringWarranties.Count);
        Assert.Equal("Item 01", resp.ExpiringWarranties[0].Name);
    }
}
