using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables.DeleteDurable;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Durables;

public class DeleteDurableHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DeleteDurableHandler _sut;

    public DeleteDurableHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteDurableHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(Guid? parentItemId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            ParentItemId = parentItemId,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    [Fact]
    public async Task Handle_DeletesCleanItem()
    {
        var item = await AddItemAsync();

        var result = await _sut.Handle(new DeleteDurableCommand(item.Id), Ct);

        Assert.True(result);
        Assert.Empty(_db.Items);
        _audit.Received(1).Deleted("Item", item.Id, "Drill", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingItem_ReturnsFalse()
    {
        var result = await _sut.Handle(new DeleteDurableCommand(Guid.NewGuid()), Ct);

        Assert.False(result);
    }

    [Fact]
    public async Task Handle_WithChildItem_Throws()
    {
        var container = await AddItemAsync();
        await AddItemAsync(parentItemId: container.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteDurableCommand(container.Id), Ct));

        // Container survives the blocked delete.
        Assert.Contains(_db.Items, i => i.Id == container.Id);
    }

    private async Task<ItemNote> AddNoteAsync(Guid itemId)
    {
        var note = new ItemNote
        {
            Id = Guid.NewGuid(), ItemId = itemId, Subject = "Oil change", Body = "",
            OccurredAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.ItemNotes.Add(note);
        await _db.SaveChangesAsync(Ct);
        return note;
    }

    [Fact]
    public async Task Handle_WithNotes_Throws()
    {
        // Notes are user-authored history (activity.md §Delete gate): the FK would cascade, so the
        // handler is what refuses and points at archive instead.
        var item = await AddItemAsync();
        await AddNoteAsync(item.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteDurableCommand(item.Id), Ct));

        Assert.Contains("notes", ex.Message);
        Assert.Contains(_db.Items, i => i.Id == item.Id);
        Assert.Single(_db.ItemNotes);
    }

    [Fact]
    public async Task Handle_AfterNotesDeleted_Deletes()
    {
        var item = await AddItemAsync();
        var note = await AddNoteAsync(item.Id);
        _db.ItemNotes.Remove(note);
        await _db.SaveChangesAsync(Ct);

        Assert.True(await _sut.Handle(new DeleteDurableCommand(item.Id), Ct));
        Assert.Empty(_db.Items);
    }
}
