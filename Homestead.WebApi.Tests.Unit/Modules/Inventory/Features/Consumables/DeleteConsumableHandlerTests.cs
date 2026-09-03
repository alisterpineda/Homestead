using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.DeleteConsumable;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Consumables;

public class DeleteConsumableHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly DeleteConsumableHandler _sut;

    public DeleteConsumableHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteConsumableHandler(_db, _audit, unitOfWork);
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

    [Fact]
    public async Task Handle_DeletesCleanConsumable()
    {
        var item = await AddConsumableAsync();

        var result = await _sut.Handle(new DeleteConsumableCommand(item.Id), Ct);

        Assert.True(result);
        Assert.Empty(_db.Items);
        _audit.Received(1).Deleted("Item", item.Id, "Screws", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingItem_ReturnsFalse()
    {
        Assert.False(await _sut.Handle(new DeleteConsumableCommand(Guid.NewGuid()), Ct));
    }

    [Fact]
    public async Task Handle_WithLedgerHistory_Throws()
    {
        var item = await AddConsumableAsync();
        _db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            Kind = MovementKind.Flow,
            Quantity = 5m,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(Ct);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteConsumableCommand(item.Id), Ct));

        Assert.Contains("stock history", ex.Message);
        Assert.Contains(_db.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Handle_WithNotes_Throws()
    {
        var item = await AddConsumableAsync();
        _db.ItemNotes.Add(new ItemNote
        {
            Id = Guid.NewGuid(), ItemId = item.Id, Body = "Bought a second box",
            OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(Ct);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteConsumableCommand(item.Id), Ct));

        Assert.Contains("notes", ex.Message);
        Assert.Contains(_db.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task Handle_AfterNotesDeleted_Deletes()
    {
        var item = await AddConsumableAsync();
        var note = new ItemNote
        {
            Id = Guid.NewGuid(), ItemId = item.Id, Body = "x",
            OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.ItemNotes.Add(note);
        await _db.SaveChangesAsync(Ct);
        _db.ItemNotes.Remove(note);
        await _db.SaveChangesAsync(Ct);

        Assert.True(await _sut.Handle(new DeleteConsumableCommand(item.Id), Ct));
        Assert.Empty(_db.Items);
    }
}
