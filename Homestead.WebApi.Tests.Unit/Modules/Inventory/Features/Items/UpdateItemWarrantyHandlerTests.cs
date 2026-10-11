using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.UpdateWarranty;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class UpdateItemWarrantyHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly UpdateItemWarrantyHandler _sut;

    public UpdateItemWarrantyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new UpdateItemWarrantyHandler(_db, currentUser, _audit, unitOfWork, new FakeTimeProvider(Now), new UpdateItemWarrantyCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingWarranty_Throws()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = Guid.NewGuid(),
        }, Ct));
    }

    [Fact]
    public async Task Handle_WarrantyOnAnotherItem_Throws()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, provider: "Bosch");

        // It exists, but not under the route's item — must read as not found, not as editable.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = other.Id, WarrantyId = warranty.Id, Provider = "hijack",
        }, Ct));
    }

    [Fact]
    public async Task Handle_ReplacesAllThreeFields_StampsUpdatedAtAndUser()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var created = Now.AddDays(-10);
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            provider: "Bosch", expiresOn: new DateOnly(2027, 1, 1), notes: "motor only",
            createdAt: created, createdByUserId: Guid.NewGuid());

        var response = await _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = warranty.Id,
            Provider = "  Home Depot  ", ExpiresOn = new DateOnly(2026, 3, 10), Notes = " extended ",
        }, Ct);

        Assert.Equal("Home Depot", response.Provider);
        Assert.Equal(new DateOnly(2026, 3, 10), response.ExpiresOn);
        Assert.Equal("extended", response.Notes);
        Assert.Equal(WarrantyStatus.Expiring, response.Status);
        Assert.Equal(created, response.CreatedAt);
        Assert.True(response.UpdatedAt > created);
        Assert.Equal(_userId, Assert.Single(_db.ItemWarranties).UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_ClearsAllThreeFields_BackToAllNull()
    {
        // PUT semantics: an omitted field is a null, not "leave it alone" — clearing back to the
        // all-null lifetime manufacturer row must be possible.
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            provider: "Bosch", expiresOn: new DateOnly(2027, 1, 1), notes: "motor only");

        var response = await _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = warranty.Id,
        }, Ct);

        Assert.Null(response.Provider);
        Assert.Null(response.ExpiresOn);
        Assert.Null(response.Notes);
        Assert.Equal(WarrantyStatus.Active, response.Status);
    }

    [Fact]
    public async Task Handle_TracksSnapshotBeforeMutating_WithItemAsParent()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id, provider: "Bosch");
        // Capture what the entity looked like at Track() time — proving the snapshot is taken before
        // the fields are overwritten, which is what makes the diff meaningful.
        string? providerAtTrackTime = "not called";
        _audit.When(a => a.Track<ItemWarrantySnapshot, ItemWarranty>(
                Arg.Any<ItemWarranty>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>()))
            .Do(ci => providerAtTrackTime = ci.Arg<ItemWarranty>().Provider);

        await _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = warranty.Id, Provider = "Home Depot",
        }, Ct);

        Assert.Equal("Bosch", providerAtTrackTime);
        _audit.Received(1).Track<ItemWarrantySnapshot, ItemWarranty>(
            Arg.Is<ItemWarranty>(w => w.Id == warranty.Id), "ItemWarranty", warranty.Id, item.Id);
    }

    [Fact]
    public async Task Handle_NoOpUpdate_LeavesUpdatedAtUntouched()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var created = Now.AddDays(-10);
        var author = Guid.NewGuid();
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            provider: "Bosch", expiresOn: new DateOnly(2027, 1, 1), notes: "motor only",
            createdAt: created, createdByUserId: author);

        // Same values, differently padded: normalises to identical, so nothing is written.
        var response = await _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = warranty.Id,
            Provider = " Bosch ", ExpiresOn = new DateOnly(2027, 1, 1), Notes = "motor only",
        }, Ct);

        Assert.Equal(created, response.UpdatedAt);
        var stored = Assert.Single(_db.ItemWarranties);
        Assert.Equal(created, stored.UpdatedAt);
        Assert.Equal(author, stored.UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_DoesNotTouchItemUpdatedAt()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var itemUpdatedAt = item.UpdatedAt;
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id);

        await _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = item.Id, WarrantyId = warranty.Id, Provider = "Bosch",
        }, Ct);

        Assert.Equal(itemUpdatedAt, (await _db.Items.FindAsync([item.Id], Ct))!.UpdatedAt);
    }

    [Fact]
    public async Task Handle_NotesTooLong_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.HandleAsync(new UpdateItemWarrantyCommand
        {
            ItemId = Guid.NewGuid(), WarrantyId = Guid.NewGuid(), Notes = new string('x', 501),
        }, Ct));
    }
}
