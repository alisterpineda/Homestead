using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class UpdateItemNoteHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly UpdateItemNoteHandler _sut;

    public UpdateItemNoteHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new UpdateItemNoteHandler(_db, currentUser, _audit, unitOfWork, new UpdateItemNoteCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_NoteOnAnotherItem_Throws()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1));

        // The note exists, but not under the route's item — must read as not found, not as editable.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = other.Id, NoteId = note.Id, OccurredAt = note.OccurredAt, Body = "x",
        }, Ct));
    }

    [Fact]
    public async Task Handle_MissingNote_Throws()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = item.Id, NoteId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, Body = "x",
        }, Ct));
    }

    [Fact]
    public async Task Handle_ReplacesBothFields_StampsUpdatedAtAndUser_SetsIsEdited()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, created,
            body: "5W-30", createdAt: created, createdByUserId: Guid.NewGuid());
        var newDate = created.AddDays(-7);

        var response = await _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = item.Id, NoteId = note.Id, OccurredAt = newDate, Body = "  belt replaced  ",
        }, Ct);

        Assert.Equal(newDate, response.OccurredAt);
        Assert.Equal("belt replaced", response.Body);
        Assert.True(response.IsEdited);
        Assert.Equal(created, response.CreatedAt);
        Assert.True(response.UpdatedAt > created);

        var stored = Assert.Single(_db.ItemNotes);
        Assert.Equal(_userId, stored.UpdatedByUserId);
        Assert.NotEqual(_userId, stored.CreatedByUserId);
    }

    [Fact]
    public async Task Handle_TracksSnapshot_WithItemAsParent()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1));

        await _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = item.Id, NoteId = note.Id, OccurredAt = note.OccurredAt, Body = "changed",
        }, Ct);

        _audit.Received(1).Track<ItemNoteSnapshot, ItemNote>(
            Arg.Is<ItemNote>(n => n.Id == note.Id), "InventoryNote", note.Id, item.Id);
    }

    [Fact]
    public async Task Handle_NoOpUpdate_LeavesUpdatedAtUntouched()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var author = Guid.NewGuid();
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, created,
            body: "5W-30", createdAt: created, createdByUserId: author);

        // Same values, differently padded: normalises to identical, so nothing is written and the note
        // must not become "edited".
        var response = await _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = item.Id, NoteId = note.Id, OccurredAt = created, Body = " 5W-30 ",
        }, Ct);

        Assert.False(response.IsEdited);
        Assert.Equal(created, response.UpdatedAt);
        var stored = Assert.Single(_db.ItemNotes);
        Assert.Equal(created, stored.UpdatedAt);
        Assert.Equal(author, stored.UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_DoesNotTouchItemUpdatedAt()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var itemUpdatedAt = item.UpdatedAt;
        var note = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1));

        await _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = item.Id, NoteId = note.Id, OccurredAt = note.OccurredAt, Body = "changed",
        }, Ct);

        Assert.Equal(itemUpdatedAt, (await _db.Items.FindAsync([item.Id], Ct))!.UpdatedAt);
    }

    [Fact]
    public async Task Handle_BlankBody_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.Handle(new UpdateItemNoteCommand
        {
            ItemId = Guid.NewGuid(), NoteId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow.AddDays(-1), Body = "   ",
        }, Ct));
    }
}
