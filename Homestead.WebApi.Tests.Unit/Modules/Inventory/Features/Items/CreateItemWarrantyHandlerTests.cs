using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.CreateWarranty;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class CreateItemWarrantyHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly CreateItemWarrantyHandler _sut;

    public CreateItemWarrantyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateItemWarrantyHandler(_db, currentUser, _audit, unitOfWork, new FakeTimeProvider(Now), new CreateItemWarrantyCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingItem_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.Handle(new CreateItemWarrantyCommand { ItemId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_OnConsumable_ThrowsBusinessRule()
    {
        // Durables only — the rule spans tables, so it lives here rather than in a check constraint.
        var consumable = await ItemNoteTestData.AddItemAsync(_db, Fungibility.Fungible);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemWarrantyCommand { ItemId = consumable.Id }, Ct));
        Assert.Equal("Warranties apply to durable items only.", ex.Message);
        Assert.Empty(_db.ItemWarranties);
    }

    [Fact]
    public async Task Handle_AllNullBody_CreatesLifetimeManufacturerWarranty()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.Handle(new CreateItemWarrantyCommand { ItemId = item.Id }, Ct);

        Assert.Equal(item.Id, response.ItemId);
        Assert.Null(response.Provider);
        Assert.Null(response.ExpiresOn);
        Assert.Null(response.Notes);
        // Open-ended cover never expires, so the row reads Active regardless of the date.
        Assert.Equal(WarrantyStatus.Active, response.Status);
        Assert.Equal(response.CreatedAt, response.UpdatedAt);
        Assert.Single(_db.ItemWarranties);
    }

    [Fact]
    public async Task Handle_TrimsProviderAndNotes_AndCollapsesBlanksToNull()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.Handle(new CreateItemWarrantyCommand
        {
            ItemId = item.Id, Provider = "  Bosch  ", Notes = "   ",
        }, Ct);

        Assert.Equal("Bosch", response.Provider);
        Assert.Null(response.Notes);
    }

    [Theory]
    // Boundaries of the 30-day window, measured from the fixed "today" of 2026-03-01.
    [InlineData("2026-02-28", WarrantyStatus.Expired)]
    [InlineData("2026-03-01", WarrantyStatus.Expiring)]
    [InlineData("2026-03-31", WarrantyStatus.Expiring)]
    [InlineData("2026-04-01", WarrantyStatus.Active)]
    public async Task Handle_ResolvesRowStatusAgainstTheServerDate(string expiresOn, WarrantyStatus expected)
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.Handle(new CreateItemWarrantyCommand
        {
            ItemId = item.Id, ExpiresOn = DateOnly.Parse(expiresOn),
        }, Ct);

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task Handle_AuditsCreated_WithItemAsParent()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        var response = await _sut.Handle(new CreateItemWarrantyCommand { ItemId = item.Id }, Ct);

        _audit.Received(1).Created("ItemWarranty", response.Id, item.Id);
    }

    [Fact]
    public async Task Handle_AttributesToCurrentUser()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        await _sut.Handle(new CreateItemWarrantyCommand { ItemId = item.Id, Provider = "Bosch" }, Ct);

        var stored = Assert.Single(_db.ItemWarranties);
        Assert.Equal(_userId, stored.CreatedByUserId);
        Assert.Equal(_userId, stored.UpdatedByUserId);
    }

    [Theory]
    [InlineData(ItemStatus.Lent)]
    [InlineData(ItemStatus.Archived)]
    public async Task Handle_WorksOnAnyDurableStatus(ItemStatus status)
    {
        var item = await ItemNoteTestData.AddItemAsync(_db, status: status);

        var response = await _sut.Handle(new CreateItemWarrantyCommand { ItemId = item.Id }, Ct);

        Assert.Equal(item.Id, response.ItemId);
    }

    [Fact]
    public async Task Handle_DoesNotTouchItemUpdatedAt()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var itemUpdatedAt = item.UpdatedAt;

        await _sut.Handle(new CreateItemWarrantyCommand { ItemId = item.Id }, Ct);

        // Paperwork about the item, not an edit of it — Browse recency is unaffected (notes precedent).
        Assert.Equal(itemUpdatedAt, (await _db.Items.FindAsync([item.Id], Ct))!.UpdatedAt);
    }

    [Fact]
    public async Task Handle_ProviderTooLong_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.Handle(new CreateItemWarrantyCommand { ItemId = Guid.NewGuid(), Provider = new string('x', 201) }, Ct));
    }
}
