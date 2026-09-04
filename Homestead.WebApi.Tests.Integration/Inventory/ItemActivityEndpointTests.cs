using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// Backend integration coverage for the item-level activity feed and its notes
// (/api/v1/inventory/items/{id}/activity[/notes]). This is the only place the item_notes FK and
// the delete gate are exercised against a real Postgres; the merge/sort logic is unit-tested in
// GetItemActivityHandlerTests.
[Collection("Integration")]
public class ItemActivityEndpointTests : IntegrationTestBase
{
    public ItemActivityEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private record NoteResult(
        Guid Id, Guid ItemId, DateTimeOffset OccurredAt, string Body,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsEdited,
        string? CreatedByDisplayName, string? CreatedByEmail,
        string? UpdatedByDisplayName, string? UpdatedByEmail);

    private record ActivityRow(
        string Type, Guid Id, DateTimeOffset Timestamp, string? UserDisplayName, string? UserEmail,
        string? Summary, string? Body, DateTimeOffset? OccurredAt,
        DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt, bool IsEdited,
        string? UpdatedByDisplayName, string? UpdatedByEmail);

    // ----- Helpers -----

    private static string ActivityUrl(Guid itemId) =>
        $"/api/v1/inventory/items/{itemId}/activity";

    private static string NotesUrl(Guid itemId, Guid? noteId = null) =>
        $"/api/v1/inventory/items/{itemId}/activity/notes" + (noteId is null ? "" : $"/{noteId}");

    private static async Task<Guid> CreateDurableAsync(HttpClient client, string name = "Cordless drill")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!.Id;
    }

    private static async Task<Guid> CreateConsumableAsync(HttpClient client, string name = "Screws")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!.Id;
    }

    private static async Task<NoteResult> CreateNoteAsync(
        HttpClient client, Guid itemId, string body = "note body", DateTimeOffset? occurredAt = null)
    {
        var resp = await client.PostAsJsonAsync(NotesUrl(itemId), new { occurredAt, body }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<NoteResult>(CT))!;
    }

    private static async Task<PaginatedResult<ActivityRow>> GetFeedAsync(HttpClient client, Guid itemId)
    {
        var resp = await client.GetAsync(ActivityUrl(itemId), CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<PaginatedResult<ActivityRow>>(CT))!;
    }

    // ----- Create -----

    [Fact]
    public async Task CreateNote_OnDurable_Returns201WithTrimmedFieldsAndAttribution()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var when = new DateTimeOffset(2024, 3, 9, 14, 0, 0, TimeSpan.Zero);

        var resp = await client.PostAsJsonAsync(NotesUrl(itemId), new
        {
            occurredAt = when, body = "  5W-30, 4 quarts  ",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Equal($"/api/v1/inventory/items/{itemId}/activity/notes/", resp.Headers.Location!.ToString()[..^36]);
        var note = (await resp.Content.ReadFromJsonAsync<NoteResult>(CT))!;
        Assert.Equal(itemId, note.ItemId);
        Assert.Equal(when, note.OccurredAt);
        Assert.Equal("5W-30, 4 quarts", note.Body);
        Assert.False(note.IsEdited);
        Assert.Equal(note.CreatedAt, note.UpdatedAt);
        Assert.NotNull(note.CreatedByDisplayName);
        Assert.NotNull(note.CreatedByEmail);
    }

    [Fact]
    public async Task CreateNote_WithNonZeroOffset_Returns201_StoredAsTheSameInstant()
    {
        // Npgsql rejects a DateTimeOffset with a non-zero offset on a timestamptz column, so the handlers
        // normalise to UTC; the instant must survive the round trip unchanged.
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var local = new DateTimeOffset(2024, 3, 9, 16, 0, 0, TimeSpan.FromHours(2));

        var note = await CreateNoteAsync(client, itemId, body: "x", occurredAt: local);

        Assert.Equal(local.ToUniversalTime(), note.OccurredAt);
        Assert.Equal(TimeSpan.Zero, note.OccurredAt.Offset);

        var put = await client.PutAsJsonAsync(NotesUrl(itemId, note.Id),
            new { occurredAt = local.AddDays(-1), body = "x" }, CT);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(local.AddDays(-1).ToUniversalTime(), (await put.Content.ReadFromJsonAsync<NoteResult>(CT))!.OccurredAt);
    }

    [Fact]
    public async Task CreateNote_OnConsumable_Returns201()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateConsumableAsync(client);

        var note = await CreateNoteAsync(client, itemId, body: "Bought a second box");

        Assert.Equal(itemId, note.ItemId);
        Assert.Equal("Bought a second box", note.Body);
    }

    [Fact]
    public async Task CreateNote_UnknownItem_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync(NotesUrl(Guid.NewGuid()), new { body = "x" }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task CreateNote_BlankBody_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.PostAsJsonAsync(NotesUrl(itemId), new { body = "  " }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateNote_FutureDate_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.PostAsJsonAsync(NotesUrl(itemId),
            new { body = "x", occurredAt = DateTimeOffset.UtcNow.AddHours(1) }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Feed -----

    [Fact]
    public async Task GetActivity_InterleavesAuditAndNotes_BackDatedNoteAfterCreation()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var backDated = await CreateNoteAsync(client, itemId, body: "Original purchase",
            occurredAt: DateTimeOffset.UtcNow.AddYears(-2));
        var recent = await CreateNoteAsync(client, itemId, body: "Sharpened the bit");

        var feed = await GetFeedAsync(client, itemId);

        Assert.Equal(3, feed.TotalCount);
        Assert.Equal(recent.Id, feed.Items[0].Id);
        Assert.Equal("note", feed.Items[0].Type);
        Assert.Equal("Sharpened the bit", feed.Items[0].Body);
        Assert.NotNull(feed.Items[0].UserDisplayName);

        Assert.Equal("audit_log", feed.Items[1].Type);
        Assert.EndsWith("added this item", feed.Items[1].Summary);

        // Typed after the item was created, but about something years earlier — sorts by OccurredAt.
        Assert.Equal(backDated.Id, feed.Items[2].Id);
        Assert.Equal("Original purchase", feed.Items[2].Body);
        Assert.Equal(backDated.OccurredAt, feed.Items[2].Timestamp);
    }

    [Fact]
    public async Task GetActivity_OnConsumable_Returns200()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateConsumableAsync(client);
        await CreateNoteAsync(client, itemId, body: "x");

        var feed = await GetFeedAsync(client, itemId);

        Assert.Contains(feed.Items, r => r.Type == "audit_log");
        Assert.Contains(feed.Items, r => r.Type == "note");
    }

    [Fact]
    public async Task GetActivity_UnknownItem_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync(ActivityUrl(Guid.NewGuid()), CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- Update -----

    [Fact]
    public async Task UpdateNote_Returns200_MarksEdited_AndFeedReflectsIt()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var note = await CreateNoteAsync(client, itemId, body: "5W-30");

        var resp = await client.PutAsJsonAsync(NotesUrl(itemId, note.Id), new
        {
            occurredAt = note.OccurredAt.AddDays(-1), body = "5W-30 and a new filter",
        }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var updated = (await resp.Content.ReadFromJsonAsync<NoteResult>(CT))!;
        Assert.Equal("5W-30 and a new filter", updated.Body);
        Assert.True(updated.IsEdited);
        Assert.True(updated.UpdatedAt > updated.CreatedAt);
        Assert.NotNull(updated.UpdatedByDisplayName);

        var feed = await GetFeedAsync(client, itemId);
        var row = Assert.Single(feed.Items, r => r.Type == "note");
        Assert.Equal("5W-30 and a new filter", row.Body);
        Assert.True(row.IsEdited);
        Assert.Equal(note.OccurredAt.AddDays(-1), row.Timestamp);
    }

    [Fact]
    public async Task UpdateNote_FutureDate_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var note = await CreateNoteAsync(client, itemId, body: "x");

        var resp = await client.PutAsJsonAsync(NotesUrl(itemId, note.Id),
            new { occurredAt = DateTimeOffset.UtcNow.AddHours(1), body = "x" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateNote_UnderAnotherItem_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var otherId = await CreateDurableAsync(client, "Other");
        var note = await CreateNoteAsync(client, itemId, body: "x");

        var resp = await client.PutAsJsonAsync(NotesUrl(otherId, note.Id),
            new { occurredAt = note.OccurredAt, body = "hijack" }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- Delete -----

    [Fact]
    public async Task DeleteNote_Returns204_RemovesFromFeed_AndAuditsWithoutProjecting()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var note = await CreateNoteAsync(client, itemId, body: "Oil change");

        var resp = await client.DeleteAsync(NotesUrl(itemId, note.Id), CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var feed = await GetFeedAsync(client, itemId);
        Assert.DoesNotContain(feed.Items, r => r.Id == note.Id);
        // Only the item's own audit rows are projected — the InventoryNote lifecycle rows are not.
        Assert.All(feed.Items, r => Assert.Equal("audit_log", r.Type));
        Assert.Single(feed.Items);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "InventoryNote" && a.EntityId == note.Id)
            .ToListAsync(CT);
        Assert.Contains(auditRows, a => a.Action == AuditAction.Created && a.ParentEntityId == itemId);
        Assert.Contains(auditRows, a => a.Action == AuditAction.Deleted && a.ParentEntityId == itemId);
    }

    [Fact]
    public async Task DeleteNote_Unknown_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.DeleteAsync(NotesUrl(itemId, Guid.NewGuid()), CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ----- Delete gate -----

    [Fact]
    public async Task DeleteDurable_WithNote_Returns400_ThenSucceedsOnceNoteIsGone()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var note = await CreateNoteAsync(client, itemId, body: "x");

        var blocked = await client.DeleteAsync($"/api/v1/inventory/durables/{itemId}", CT);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Contains("notes", problem!.Detail);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(NotesUrl(itemId, note.Id), CT)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/inventory/durables/{itemId}", CT)).StatusCode);
    }

    [Fact]
    public async Task DeleteConsumable_WithNote_Returns400_ThenSucceedsOnceNoteIsGone()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateConsumableAsync(client);
        var note = await CreateNoteAsync(client, itemId, body: "x");

        var blocked = await client.DeleteAsync($"/api/v1/inventory/consumables/{itemId}", CT);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Contains("notes", problem!.Detail);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(NotesUrl(itemId, note.Id), CT)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/inventory/consumables/{itemId}", CT)).StatusCode);
    }

    // ----- Auth matrix (Contributor gate on writes; ActiveUser admits Viewer on reads) -----

    [Fact]
    public async Task CreateNote_AsViewer_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.PostAsJsonAsync(NotesUrl(itemId), new { body = "x" }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteNote_AsViewer_Return403()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        var note = await CreateNoteAsync(admin, itemId, body: "x");
        var viewer = await CreateViewerClientAsync();

        var put = await viewer.PutAsJsonAsync(NotesUrl(itemId, note.Id), new { occurredAt = note.OccurredAt, body = "y" }, CT);
        var del = await viewer.DeleteAsync(NotesUrl(itemId, note.Id), CT);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    [Fact]
    public async Task GetActivity_AsViewer_Returns200()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        await CreateNoteAsync(admin, itemId, body: "Oil change");
        var viewer = await CreateViewerClientAsync();

        var feed = await viewer.GetAsync(ActivityUrl(itemId), CT);

        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
    }

    [Fact]
    public async Task Activity_Anonymous_Returns401()
    {
        var anonymous = CreateAnonymousClient();

        var get = await anonymous.GetAsync(ActivityUrl(Guid.NewGuid()), CT);
        var post = await anonymous.PostAsJsonAsync(NotesUrl(Guid.NewGuid()), new { body = "x" }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task CreateNote_AsInactiveContributor_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        var inactive = await CreateUserClientAsync(isActive: false);

        var resp = await inactive.PostAsJsonAsync(NotesUrl(itemId), new { body = "x" }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ----- Regression: the durable-only route is gone -----

    [Fact]
    public async Task GetDurableActivity_Returns404_NowThatTheItemFeedReplacedIt()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.GetAsync($"/api/v1/inventory/durables/{itemId}/activity", CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
