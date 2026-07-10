using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Services;

/// <summary>
/// StockService has no broad unit coverage by construction (its decrement funnel relies on
/// real-database semantics InMemory can't honour — see the integration suite). These cover only the
/// holder-typing gate, which throws (or takes the InMemory-safe Add path) before any guarded update.
/// </summary>
public class StockServiceTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly StockService _sut;

    public StockServiceTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _sut = new StockService(_db, currentUser, Substitute.For<IAuditContext>());
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddConsumableAsync()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Screws",
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
            Unit = "units",
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private async Task<Item> AddHolderAsync(DurableKind kind)
    {
        var holder = new Item
        {
            Id = Guid.NewGuid(),
            Name = kind.ToString(),
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = kind,
        };
        _db.Items.Add(holder);
        await _db.SaveChangesAsync(Ct);
        return holder;
    }

    [Fact]
    public async Task ReceiveAsync_IntoAssembly_Throws()
    {
        var screws = await AddConsumableAsync();
        var pc = await AddHolderAsync(DurableKind.Assembly);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.ReceiveAsync(screws.Id, new StockReceiveRequest(
                StockLocation.InContainer(pc.Id), 5m, null, null, null, null, null), Ct));
    }

    [Fact]
    public async Task ReceiveAsync_IntoPlainItem_Throws()
    {
        var screws = await AddConsumableAsync();
        var plain = await AddHolderAsync(DurableKind.Plain);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.ReceiveAsync(screws.Id, new StockReceiveRequest(
                StockLocation.InContainer(plain.Id), 5m, null, null, null, null, null), Ct));
    }

    [Fact]
    public async Task ReceiveAsync_IntoContainer_CreatesStockEntry()
    {
        var screws = await AddConsumableAsync();
        var bin = await AddHolderAsync(DurableKind.Container);

        await _sut.ReceiveAsync(screws.Id, new StockReceiveRequest(
            StockLocation.InContainer(bin.Id), 5m, null, null, null, null, null), Ct);
        await _db.SaveChangesAsync(Ct);

        var entry = Assert.Single(_db.StockEntries);
        Assert.Equal(screws.Id, entry.ItemId);
        Assert.Equal(bin.Id, entry.ContainerItemId);
        Assert.Equal(5m, entry.Quantity);
    }

    [Fact]
    public async Task ReceiveAsync_WithSource_PersistsTrimmedSource()
    {
        var screws = await AddConsumableAsync();
        var bin = await AddHolderAsync(DurableKind.Container);

        await _sut.ReceiveAsync(screws.Id, new StockReceiveRequest(
            StockLocation.InContainer(bin.Id), 5m, null, null, null, null, "  Costco  "), Ct);
        await _db.SaveChangesAsync(Ct);

        var movement = Assert.Single(_db.StockMovements);
        Assert.Equal("Costco", movement.Source);
    }

    [Fact]
    public async Task ReceiveAsync_OverlongSource_Throws()
    {
        var screws = await AddConsumableAsync();
        var bin = await AddHolderAsync(DurableKind.Container);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.ReceiveAsync(screws.Id, new StockReceiveRequest(
                StockLocation.InContainer(bin.Id), 5m, null, null, null, null, new string('a', 257)), Ct));
    }

    [Fact]
    public async Task AdjustAsync_MovementHasNullSource()
    {
        // Adjust-up from nothing takes the InMemory-safe AddNewEntry path; only the deliberate
        // receive writes Source, so a bookkeeping adjustment must leave it null.
        var screws = await AddConsumableAsync();
        var bin = await AddHolderAsync(DurableKind.Container);

        await _sut.AdjustAsync(screws.Id, new StockAdjustRequest(
            StockLocation.InContainer(bin.Id), 7m, null, "recount"), Ct);
        await _db.SaveChangesAsync(Ct);

        var movement = Assert.Single(_db.StockMovements);
        Assert.Equal(MovementKind.Adjustment, movement.Kind);
        Assert.Null(movement.Source);
    }
}
