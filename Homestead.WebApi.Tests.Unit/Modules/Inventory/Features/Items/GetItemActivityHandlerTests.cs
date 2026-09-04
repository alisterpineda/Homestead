using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

// Merge order, tiebreaks, LEFT joins, and paging are covered here; ItemActivityEndpointTests proves
// the same feed end-to-end against a real Postgres.
public class GetItemActivityHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetItemActivityHandler _sut;

    public GetItemActivityHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetItemActivityHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task AddAuditAsync(
        Guid itemId, AuditAction action, Guid? userId, DateTimeOffset timestamp,
        List<FieldChange>? changes = null, string entityType = "Item", Guid? parentEntityId = null,
        Guid? entityId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId ?? itemId,
            ParentEntityId = parentEntityId,
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
        var result = await _sut.Handle(new GetItemActivityQuery { Id = Guid.NewGuid() }, Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_ReturnsFeed_ForAFungibleConsumable()
    {
        // Unlike the durable-only endpoint this replaces, a consumable has a feed too: its metadata
        // audit rows plus its notes (the movement ledger stays on its own tab).
        var consumable = await ItemNoteTestData.AddItemAsync(_db, Fungibility.Fungible);
        await AddAuditAsync(consumable.Id, AuditAction.Created, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1));
        await ItemNoteTestData.AddNoteAsync(_db, consumable.Id, DateTimeOffset.UtcNow, body: "Bought a second box");

        var result = await _sut.Handle(new GetItemActivityQuery { Id = consumable.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("note", result.Items[0].Type);
        Assert.Equal("audit_log", result.Items[1].Type);
    }

    [Fact]
    public async Task Handle_InterleavesByEffectiveDate_BackDatedNoteSortsBelowCreation()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        var now = DateTimeOffset.UtcNow;

        await AddAuditAsync(item.Id, AuditAction.Created, alice.Id, now.AddDays(-10));
        await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, now.AddDays(-2),
            changes: [new ScalarFieldChange("Name", "Cordless drill", "Drill")]);
        // Typed today, but about something that happened years before the item was entered: the feed
        // sorts on OccurredAt, so it lands at the bottom, below "added this item".
        var backDated = await ItemNoteTestData.AddNoteAsync(_db, item.Id, now.AddYears(-3),
            body: "Original purchase", createdAt: now, createdByUserId: alice.Id);
        var recent = await ItemNoteTestData.AddNoteAsync(_db, item.Id, now.AddDays(-1),
            body: "Oil change", createdAt: now, createdByUserId: alice.Id);

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(["note", "audit_log", "audit_log", "note"], result.Items.Select(i => i.Type));
        Assert.Equal(recent.Id, result.Items[0].Id);
        Assert.Equal("Alice updated name", result.Items[1].Summary);
        Assert.Equal("Alice added this item", result.Items[2].Summary);
        Assert.Equal(backDated.Id, result.Items[3].Id);

        var note = result.Items[0];
        Assert.Equal("Oil change", note.Body);
        Assert.Equal(now.AddDays(-1), note.Timestamp);
        Assert.Equal(now.AddDays(-1), note.OccurredAt);
        Assert.Equal(now, note.CreatedAt);
        Assert.Equal("Alice", note.UserDisplayName);
        Assert.Equal("alice@test.com", note.UserEmail);
        Assert.Null(note.Summary);
    }

    [Fact]
    public async Task Handle_CreatedAtBreaksOccurredAtTie_BetweenNotes()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var sameDay = DateTimeOffset.UtcNow.AddDays(-5);
        var older = await ItemNoteTestData.AddNoteAsync(_db, item.Id, sameDay, body: "first typed",
            createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = await ItemNoteTestData.AddNoteAsync(_db, item.Id, sameDay, body: "second typed",
            createdAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal([newer.Id, older.Id], result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Handle_OrdersDeterministically_WhenEffectiveAndRecordedTimesTie()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        var sameInstant = DateTimeOffset.UtcNow;
        // Three audit rows and two notes all on one instant (a coarse clock or a same-tick batch).
        for (var i = 0; i < 3; i++)
            await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, sameInstant,
                changes: [new ScalarFieldChange("Notes", null, $"note {i}")]);
        for (var i = 0; i < 2; i++)
            await ItemNoteTestData.AddNoteAsync(_db, item.Id, sameInstant, body: $"n{i}", createdAt: sameInstant);

        var first = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);
        var second = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(5, first.TotalCount);
        // The Id tiebreaker makes the order stable across requests, so paging can't drop/duplicate ties.
        Assert.Equal(first.Items.Select(x => x.Id), second.Items.Select(x => x.Id));
        Assert.Equal(
            first.Items.Select(x => x.Id).OrderByDescending(id => id),
            first.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Handle_LeftJoinsUsers_NullActorAuditRowAndDeletedCreatorNoteStillRender()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        // A system-actor audit row (null UserId) and a note whose creator no longer exists (a UserId with
        // no matching Users row — the SetNull FK would have nulled it, but a dangling id must not drop
        // the row either).
        await AddAuditAsync(item.Id, AuditAction.Updated, userId: null, DateTimeOffset.UtcNow.AddHours(-1),
            changes: [new ScalarFieldChange("Status", "Active", "Archived")]);
        await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow, body: "orphaned",
            createdByUserId: Guid.NewGuid());

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i =>
        {
            Assert.Null(i.UserDisplayName);
            Assert.Null(i.UserEmail);
        });
        Assert.Equal("Someone updated status", result.Items[1].Summary);
        Assert.Equal("orphaned", result.Items[0].Body);
    }

    [Fact]
    public async Task Handle_IsEdited_TrueOnlyWhenUpdatedAtDiffersFromCreatedAt()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var bob = await ItemNoteTestData.AddUserAsync(_db, "Bob", "bob@test.com");
        var created = DateTimeOffset.UtcNow.AddHours(-2);
        var pristine = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-1),
            body: "pristine", createdAt: created);
        var edited = await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddDays(-2),
            body: "edited", createdAt: created, updatedAt: created.AddMinutes(5), updatedByUserId: bob.Id);

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        var pristineRow = Assert.Single(result.Items, i => i.Id == pristine.Id);
        Assert.False(pristineRow.IsEdited);
        var editedRow = Assert.Single(result.Items, i => i.Id == edited.Id);
        Assert.True(editedRow.IsEdited);
        Assert.Equal(created.AddMinutes(5), editedRow.UpdatedAt);
        Assert.Equal("Bob", editedRow.UpdatedByDisplayName);
        Assert.Equal("bob@test.com", editedRow.UpdatedByEmail);
    }

    [Fact]
    public async Task Handle_DoesNotProjectInventoryNoteAuditRows()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        await AddAuditAsync(item.Id, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow.AddDays(-1));
        // The note lifecycle is audited under "InventoryNote" with the item as parent — those rows are
        // deliberately NOT feed entries (the note itself is; a deleted note simply disappears).
        await AddAuditAsync(item.Id, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow,
            entityType: "InventoryNote", entityId: Guid.NewGuid(), parentEntityId: item.Id);
        await AddAuditAsync(item.Id, AuditAction.Deleted, alice.Id, DateTimeOffset.UtcNow,
            entityType: "InventoryNote", entityId: Guid.NewGuid(), parentEntityId: item.Id);

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        var only = Assert.Single(result.Items);
        Assert.Equal("Alice added this item", only.Summary);
    }

    [Fact]
    public async Task Handle_PaginatesInMemory_AcrossBothSources()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        for (var i = 0; i < 3; i++)
            await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-i),
                changes: [new ScalarFieldChange("Notes", null, $"note {i}")]);
        for (var i = 3; i < 5; i++)
            await ItemNoteTestData.AddNoteAsync(_db, item.Id, DateTimeOffset.UtcNow.AddMinutes(-i), body: $"n{i}");

        var page = await _sut.Handle(new GetItemActivityQuery { Id = item.Id, Page = 2, PageSize = 2 }, Ct);

        Assert.NotNull(page);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task Handle_BuildsDedupedUpdatedSummary_ForPairedFields()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        // A value + currency + location change in one PATCH: paired fields collapse to one label each,
        // so the summary reads "value, location" rather than repeating them.
        await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow,
            changes:
            [
                new ScalarFieldChange("Value", null, "100"),
                new ScalarFieldChange("ValueCurrencyId", null, Guid.NewGuid().ToString()),
                new ScalarFieldChange("AreaId", null, Guid.NewGuid().ToString()),
            ]);

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal("Alice updated value, location", Assert.Single(result.Items).Summary);
    }

    [Fact]
    public async Task Handle_LabelsNotesFieldAsDescription()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var alice = await ItemNoteTestData.AddUserAsync(_db, "Alice", "alice@test.com");
        // The audit field is still "Notes" (column name), but every UI surface labels it Description.
        await AddAuditAsync(item.Id, AuditAction.Updated, alice.Id, DateTimeOffset.UtcNow,
            changes: [new ScalarFieldChange("Notes", null, "x")]);

        var result = await _sut.Handle(new GetItemActivityQuery { Id = item.Id }, Ct);

        Assert.NotNull(result);
        Assert.Equal("Alice updated description", Assert.Single(result.Items).Summary);
    }
}
