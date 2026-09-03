using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurableActivity;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Durables;

public class GetDurableActivityHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetDurableActivityHandler _sut;

    public GetDurableActivityHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetDurableActivityHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(Fungibility fungibility = Fungibility.NonFungible)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Cordless drill",
            Fungibility = fungibility,
            Status = ItemStatus.Active,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private async Task<AppUser> AddUserAsync(string displayName, string email)
    {
        var user = new AppUser { Id = Guid.NewGuid(), DisplayName = displayName, Email = email };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(Ct);
        return user;
    }

    private async Task AddAuditAsync(
        Guid itemId, AuditAction action, Guid? userId, DateTimeOffset timestamp,
        List<FieldChange>? changes = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "Item",
            EntityId = itemId,
            Action = action,
            UserId = userId,
            Timestamp = timestamp,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, FieldChange.JsonOptions),
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenItemMissing()
    {
        var result = await _sut.Handle(new GetDurableActivityQuery { Id = Guid.NewGuid() }, Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ReturnsNull_ForAFungibleConsumable()
    {
        // Consumables keep a movement ledger instead; the item-activity endpoint 404s for them.
        var consumable = await AddItemAsync(Fungibility.Fungible);
        await AddAuditAsync(consumable.Id, AuditAction.Created, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var result = await _sut.Handle(new GetDurableActivityQuery { Id = consumable.Id }, Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_LeftJoinsUsers_NullAndDeletedActorsStillRender()
    {
        var item = await AddItemAsync();
        var alice = await AddUserAsync("Alice", "alice@test.com");

        // Three rows: a known actor, a null (system) actor, and a since-deleted actor (a UserId with
        // no matching Users row). A LEFT join must keep all three; an inner join would drop the last two.
        await AddAuditAsync(item.Id, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-3));
        await AddAuditAsync(item.Id, AuditAction.Updated, userId: null, DateTimeOffset.UtcNow.AddMinutes(-2),
            changes: [new ScalarFieldChange("Status", "Active", "Archived")]);
        await AddAuditAsync(item.Id, AuditAction.Updated, userId: Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1),
            changes: [new ScalarFieldChange("Name", "Cordless drill", "Drill")]);

        var result = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);

        var anonymous = result.Items.Where(i => i.UserDisplayName is null && i.UserEmail is null).ToList();
        Assert.Equal(2, anonymous.Count); // null actor + deleted actor — neither vanished
        Assert.All(anonymous, i => Assert.StartsWith("Someone ", i.Summary));

        var byAlice = Assert.Single(result.Items, i => i.UserDisplayName == "Alice");
        Assert.Equal("alice@test.com", byAlice.UserEmail);
        Assert.Equal("Alice added this item", byAlice.Summary);
    }

    [Fact]
    public async Task Handle_OrdersNewestFirst_AndBuildsDedupedUpdatedSummary()
    {
        var item = await AddItemAsync();
        var alice = await AddUserAsync("Alice", "alice@test.com");

        await AddAuditAsync(item.Id, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-5));
        // A value + currency + location change in one PATCH: paired fields collapse to one label each,
        // so the summary reads "value, location" rather than repeating them.
        await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-1),
            changes:
            [
                new ScalarFieldChange("Value", null, "100"),
                new ScalarFieldChange("ValueCurrencyId", null, Guid.NewGuid().ToString()),
                new ScalarFieldChange("AreaId", null, Guid.NewGuid().ToString()),
            ]);

        var result = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        // Newest-first: the update precedes the creation.
        Assert.Equal("Alice updated value, location", result.Items[0].Summary);
        Assert.Equal("Alice added this item", result.Items[1].Summary);
    }

    [Fact]
    public async Task Handle_LabelsNotesFieldAsDescription()
    {
        var item = await AddItemAsync();
        var alice = await AddUserAsync("Alice", "alice@test.com");
        // The audit field is still "Notes" (column name), but every UI surface labels it Description.
        await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow,
            changes: [new ScalarFieldChange("Notes", null, "x")]);

        var result = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal("Alice updated description", Assert.Single(result.Items).Summary);
    }

    [Fact]
    public async Task Handle_OrdersDeterministically_WhenTimestampsTie()
    {
        var item = await AddItemAsync();
        var alice = await AddUserAsync("Alice", "alice@test.com");
        // Three rows sharing one timestamp (a coarse-resolution clock or a same-tick batch).
        var sameInstant = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
            await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, sameInstant,
                changes: [new ScalarFieldChange("Notes", null, $"note {i}")]);

        var first = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id }, Ct);
        var second = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(first);
        Assert.NotNull(second);
        // The Id tiebreaker makes the order stable across requests, so paging can't drop/duplicate ties.
        Assert.Equal(first.Items.Select(x => x.Id), second.Items.Select(x => x.Id));
        Assert.Equal(
            first.Items.Select(x => x.Id).OrderByDescending(id => id),
            first.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Handle_PaginatesInMemory()
    {
        var item = await AddItemAsync();
        var alice = await AddUserAsync("Alice", "alice@test.com");
        for (var i = 0; i < 5; i++)
            await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-i),
                changes: [new ScalarFieldChange("Notes", null, $"note {i}")]);

        var page = await _sut.Handle(new GetDurableActivityQuery { Id = item.Id, Page = 2, PageSize = 2 }, Ct);

        Assert.NotNull(page);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
    }
}
