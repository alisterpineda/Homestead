using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class CreateItemNoteHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly CreateItemNoteHandler _sut;

    public CreateItemNoteHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateItemNoteHandler(_db, currentUser, _audit, unitOfWork, new CreateItemNoteCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingItem_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.HandleAsync(new CreateItemNoteCommand { ItemId = Guid.NewGuid(), Body = "x" }, Ct));
    }

    [Fact]
    public async Task Handle_NullOccurredAt_StoresNow()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var before = DateTimeOffset.UtcNow;

        var response = await _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "Replaced belt" }, Ct);

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(response.OccurredAt, before, after);
        Assert.Equal(response.CreatedAt, response.OccurredAt);
        Assert.Equal(response.CreatedAt, response.UpdatedAt);
        Assert.False(response.IsEdited);
    }

    [Fact]
    public async Task Handle_ExplicitOccurredAt_StoredAsGiven()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var when = new DateTimeOffset(2021, 4, 5, 10, 30, 0, TimeSpan.Zero);

        var response = await _sut.HandleAsync(new CreateItemNoteCommand
        {
            ItemId = item.Id, OccurredAt = when, Body = "5W-30",
        }, Ct);

        Assert.Equal(when, response.OccurredAt);
        Assert.NotEqual(when, response.CreatedAt);
        Assert.Equal(when, Assert.Single(_db.ItemNotes).OccurredAt);
    }

    [Fact]
    public async Task Handle_TrimsBody()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.HandleAsync(new CreateItemNoteCommand
        {
            ItemId = item.Id, Body = "  needs a new blade  \n",
        }, Ct);

        Assert.Equal("needs a new blade", response.Body);
        Assert.Equal(item.Id, response.ItemId);
    }

    [Fact]
    public async Task Handle_AuditsCreated_WithItemAsParent()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "x" }, Ct);

        _audit.Received(1).Created("InventoryNote", response.Id, item.Id);
    }

    [Fact]
    public async Task Handle_AttributesToCurrentUser_AndResolvesDisplayName()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        _db.Users.Add(new Domain.Identity.AppUser { Id = _userId, DisplayName = "Alice", Email = "alice@test.com" });
        await _db.SaveChangesAsync(Ct);

        var response = await _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "x" }, Ct);

        var note = Assert.Single(_db.ItemNotes);
        Assert.Equal(_userId, note.CreatedByUserId);
        Assert.Equal(_userId, note.UpdatedByUserId);
        Assert.Equal("Alice", response.CreatedByDisplayName);
        Assert.Equal("alice@test.com", response.CreatedByEmail);
        Assert.Equal("Alice", response.UpdatedByDisplayName);
    }

    [Theory]
    [InlineData(Fungibility.Fungible, ItemStatus.Active)]
    [InlineData(Fungibility.NonFungible, ItemStatus.Lent)]
    [InlineData(Fungibility.NonFungible, ItemStatus.Archived)]
    public async Task Handle_WorksOnAnyKindAndStatus(Fungibility fungibility, ItemStatus status)
    {
        // Notes complete an item's story, so a consumable, a lent durable, and an archived item all
        // take them (activity.md §Which items can carry notes).
        var item = await ItemNoteTestData.AddItemAsync(_db, fungibility, status);

        var response = await _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "x" }, Ct);

        Assert.Equal(item.Id, response.ItemId);
    }

    [Fact]
    public async Task Handle_DoesNotTouchItemUpdatedAt()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var itemUpdatedAt = item.UpdatedAt;

        await _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "x" }, Ct);

        // A note is history about the item, not an edit of it — Browse recency is unaffected.
        Assert.Equal(itemUpdatedAt, (await _db.Items.FindAsync([item.Id], Ct))!.UpdatedAt);
    }

    [Fact]
    public async Task Handle_BlankBody_ThrowsValidationException()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.HandleAsync(new CreateItemNoteCommand { ItemId = item.Id, Body = "   " }, Ct));
    }
}
