using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// Backend integration coverage for item warranties
// (/api/v1/inventory/items/{id}/warranties). This is the only place the item_warranties FK, its
// cascade, and the derived status reach a real Postgres; the rules themselves are unit-tested in
// WarrantyStatusRulesTests and the handler suites.
[Collection("Integration")]
public class ItemWarrantiesEndpointTests : IntegrationTestBase
{
    public ItemWarrantiesEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private record WarrantyResult(
        Guid Id, Guid ItemId, string? Provider, DateOnly? ExpiresOn, string? Notes,
        string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private record ActivityRow(string Type, Guid Id, string? Summary);

    private record WarrantyOverviewResult(
        int ExpiringWarrantyCount, IReadOnlyList<ExpiringWarrantyItem> ExpiringWarranties);

    private record ExpiringWarrantyItem(Guid Id, string Name, DateOnly ExpiresOn);

    // ----- Helpers -----

    private static string WarrantiesUrl(Guid itemId, Guid? warrantyId = null) =>
        $"/api/v1/inventory/items/{itemId}/warranties" + (warrantyId is null ? "" : $"/{warrantyId}");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> CreateDurableAsync(HttpClient client, string name = "Dryer")
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

    private static async Task<WarrantyResult> CreateWarrantyAsync(
        HttpClient client, Guid itemId, string? provider = null, DateOnly? expiresOn = null, string? notes = null)
    {
        var resp = await client.PostAsJsonAsync(WarrantiesUrl(itemId), new { provider, expiresOn, notes }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<WarrantyResult>(CT))!;
    }

    private static async Task<IReadOnlyList<WarrantyResult>> ListWarrantiesAsync(HttpClient client, Guid itemId)
    {
        var resp = await client.GetAsync(WarrantiesUrl(itemId), CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<List<WarrantyResult>>(CT))!;
    }

    private static async Task<DurableItem> GetItemDurableAsync(HttpClient client, Guid itemId)
    {
        var resp = await client.GetAsync($"/api/v1/inventory/items/{itemId}", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = (await resp.Content.ReadFromJsonAsync<ItemDetailItem>(CT))!;
        Assert.NotNull(detail.Durable);
        return detail.Durable;
    }

    // ----- Create -----

    [Fact]
    public async Task CreateWarranty_Returns201WithLocationAndTrimmedFields()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.PostAsJsonAsync(WarrantiesUrl(itemId), new
        {
            provider = "  Bosch  ", expiresOn = "2028-03-12", notes = "  motor only  ",
        }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var warranty = (await resp.Content.ReadFromJsonAsync<WarrantyResult>(CT))!;
        Assert.Equal($"/api/v1/inventory/items/{itemId}/warranties/{warranty.Id}", resp.Headers.Location!.ToString());
        Assert.Equal(itemId, warranty.ItemId);
        Assert.Equal("Bosch", warranty.Provider);
        Assert.Equal(new DateOnly(2028, 3, 12), warranty.ExpiresOn);
        Assert.Equal("motor only", warranty.Notes);
        Assert.Equal("Active", warranty.Status);
        Assert.Equal(warranty.CreatedAt, warranty.UpdatedAt);
    }

    [Fact]
    public async Task CreateWarranty_EmptyBody_Returns201_AsLifetimeManufacturerRow()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.PostAsJsonAsync(WarrantiesUrl(itemId), new { }, CT);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var warranty = (await resp.Content.ReadFromJsonAsync<WarrantyResult>(CT))!;
        Assert.Null(warranty.Provider);
        Assert.Null(warranty.ExpiresOn);
        Assert.Null(warranty.Notes);
        // No expiry means open-ended cover, so the row is Active forever.
        Assert.Equal("Active", warranty.Status);
    }

    [Fact]
    public async Task CreateWarranty_OnConsumable_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateConsumableAsync(client);

        var resp = await client.PostAsJsonAsync(WarrantiesUrl(itemId), new { provider = "Bosch" }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Equal("Warranties apply to durable items only.", problem!.Detail);
    }

    [Fact]
    public async Task CreateWarranty_UnknownItem_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.PostAsJsonAsync(WarrantiesUrl(Guid.NewGuid()), new { }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task CreateWarranty_ProviderTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.PostAsJsonAsync(WarrantiesUrl(itemId),
            new { provider = new string('a', 201) }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateWarranty_PastExpiry_Returns201_AsExpired()
    {
        // Back-filling a lapsed warranty is legitimate — there is no date rule on ExpiresOn.
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var warranty = await CreateWarrantyAsync(client, itemId, expiresOn: Today.AddDays(-1));

        Assert.Equal("Expired", warranty.Status);
    }

    // ----- List -----

    [Fact]
    public async Task GetWarranties_OrdersByExpiryAscending_NullsLast_WithPerRowStatus()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var far = await CreateWarrantyAsync(client, itemId, provider: "Bosch", expiresOn: Today.AddYears(2));
        var openEnded = await CreateWarrantyAsync(client, itemId);
        var lapsed = await CreateWarrantyAsync(client, itemId, expiresOn: Today.AddDays(-5));
        var soon = await CreateWarrantyAsync(client, itemId, expiresOn: Today.AddDays(10));

        var warranties = await ListWarrantiesAsync(client, itemId);

        Assert.Equal([lapsed.Id, soon.Id, far.Id, openEnded.Id], warranties.Select(w => w.Id));
        Assert.Equal(["Expired", "Expiring", "Active", "Active"], warranties.Select(w => w.Status));
    }

    [Fact]
    public async Task GetWarranties_UnknownItem_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var resp = await client.GetAsync(WarrantiesUrl(Guid.NewGuid()), CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetWarranties_OnConsumable_Returns200Empty()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateConsumableAsync(client);

        Assert.Empty(await ListWarrantiesAsync(client, itemId));
    }

    // ----- Update -----

    [Fact]
    public async Task UpdateWarranty_Returns200_ReplacingAllThreeFields()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var warranty = await CreateWarrantyAsync(client, itemId, provider: "Bosch",
            expiresOn: Today.AddYears(2), notes: "motor only");

        var resp = await client.PutAsJsonAsync(WarrantiesUrl(itemId, warranty.Id), new
        {
            provider = "Home Depot", expiresOn = Today.AddDays(5), notes = "extended plan",
        }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var updated = (await resp.Content.ReadFromJsonAsync<WarrantyResult>(CT))!;
        Assert.Equal("Home Depot", updated.Provider);
        Assert.Equal(Today.AddDays(5), updated.ExpiresOn);
        Assert.Equal("extended plan", updated.Notes);
        Assert.Equal("Expiring", updated.Status);
        Assert.True(updated.UpdatedAt > updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateWarranty_OmittedFields_ClearThemToNull()
    {
        // PUT, not PATCH: an absent field is a null, so a row can be cleared back to all-null.
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var warranty = await CreateWarrantyAsync(client, itemId, provider: "Bosch",
            expiresOn: Today.AddYears(2), notes: "motor only");

        var resp = await client.PutAsJsonAsync(WarrantiesUrl(itemId, warranty.Id), new { }, CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var updated = (await resp.Content.ReadFromJsonAsync<WarrantyResult>(CT))!;
        Assert.Null(updated.Provider);
        Assert.Null(updated.ExpiresOn);
        Assert.Null(updated.Notes);
        Assert.Equal("Active", updated.Status);
    }

    [Fact]
    public async Task UpdateWarranty_UnderAnotherItem_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var otherId = await CreateDurableAsync(client, "Washer");
        var warranty = await CreateWarrantyAsync(client, itemId, provider: "Bosch");

        var resp = await client.PutAsJsonAsync(WarrantiesUrl(otherId, warranty.Id),
            new { provider = "hijack" }, CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateWarranty_NotesTooLong_Returns400()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var warranty = await CreateWarrantyAsync(client, itemId);

        var resp = await client.PutAsJsonAsync(WarrantiesUrl(itemId, warranty.Id),
            new { notes = new string('a', 501) }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ----- Delete -----

    [Fact]
    public async Task DeleteWarranty_Returns204_AndAuditsWithDescription()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var warranty = await CreateWarrantyAsync(client, itemId,
            provider: "Bosch", expiresOn: new DateOnly(2028, 3, 12));

        var resp = await client.DeleteAsync(WarrantiesUrl(itemId, warranty.Id), CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Empty(await ListWarrantiesAsync(client, itemId));

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "ItemWarranty" && a.EntityId == warranty.Id)
            .ToListAsync(CT);
        Assert.Contains(auditRows, a => a.Action == AuditAction.Created && a.ParentEntityId == itemId);
        Assert.Contains(auditRows, a => a.Action == AuditAction.Deleted && a.ParentEntityId == itemId);
    }

    [Fact]
    public async Task DeleteWarranty_Unknown_Returns404()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        var resp = await client.DeleteAsync(WarrantiesUrl(itemId, Guid.NewGuid()), CT);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteDurable_WithWarranties_Returns204_AndCascades()
    {
        // No delete gate (unlike notes) — the FK cascade takes the rows with the item.
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        await CreateWarrantyAsync(client, itemId, provider: "Bosch", expiresOn: Today.AddYears(1));
        await CreateWarrantyAsync(client, itemId);

        var resp = await client.DeleteAsync($"/api/v1/inventory/durables/{itemId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.ItemWarranties.AsNoTracking().Where(w => w.ItemId == itemId).ToListAsync(CT));
    }

    // ----- Derived status on the item detail -----

    [Fact]
    public async Task GetItem_ExposesDurableWarrantyStatus()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);

        Assert.Equal("None", (await GetItemDurableAsync(client, itemId)).WarrantyStatus);

        var lapsed = await CreateWarrantyAsync(client, itemId, expiresOn: Today.AddDays(-1));
        Assert.Equal("Expired", (await GetItemDurableAsync(client, itemId)).WarrantyStatus);

        // The latest expiry decides, so adding live cover flips the item even though the lapsed row stays.
        var soon = await CreateWarrantyAsync(client, itemId, expiresOn: Today.AddDays(10));
        Assert.Equal("Expiring", (await GetItemDurableAsync(client, itemId)).WarrantyStatus);

        await client.PutAsJsonAsync(WarrantiesUrl(itemId, soon.Id),
            new { expiresOn = Today.AddYears(1) }, CT);
        Assert.Equal("Active", (await GetItemDurableAsync(client, itemId)).WarrantyStatus);

        // An open-ended row wins outright.
        await client.DeleteAsync(WarrantiesUrl(itemId, soon.Id), CT);
        await CreateWarrantyAsync(client, itemId);
        Assert.Equal("Active", (await GetItemDurableAsync(client, itemId)).WarrantyStatus);
        Assert.Equal(2, (await ListWarrantiesAsync(client, itemId)).Count);
        Assert.Contains(await ListWarrantiesAsync(client, itemId), w => w.Id == lapsed.Id);
    }

    // ----- Overview tile -----

    [Fact]
    public async Task GetOverview_CountsExpiringWarranties_AndIgnoresOpenEndedOnes()
    {
        // Captured once, and kept clear of the ±30-day edge on both sides, so a run that straddles
        // UTC midnight between seeding and the request can't move a row across the window. The
        // exact edges are pinned by the deterministic handler unit tests.
        var today = Today;
        var client = await CreateAdminClientAsync();
        var expiring = await CreateDurableAsync(client, "Dryer");
        await CreateWarrantyAsync(client, expiring, expiresOn: today.AddDays(7));
        var lifetime = await CreateDurableAsync(client, "Cast iron pan");
        await CreateWarrantyAsync(client, lifetime, expiresOn: today.AddDays(3));
        await CreateWarrantyAsync(client, lifetime);
        var far = await CreateDurableAsync(client, "Fridge");
        await CreateWarrantyAsync(client, far, expiresOn: today.AddDays(45));

        var resp = await client.GetAsync("/api/v1/inventory/overview", CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var overview = (await resp.Content.ReadFromJsonAsync<WarrantyOverviewResult>(CT))!;

        Assert.Equal(1, overview.ExpiringWarrantyCount);
        var preview = Assert.Single(overview.ExpiringWarranties);
        Assert.Equal(expiring, preview.Id);
        Assert.Equal("Dryer", preview.Name);
        Assert.Equal(today.AddDays(7), preview.ExpiresOn);
    }

    // ----- Activity feed projection -----

    [Fact]
    public async Task Activity_ProjectsTheWarrantyLifecycle()
    {
        var client = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(client);
        var warranty = await CreateWarrantyAsync(client, itemId,
            provider: "Bosch", expiresOn: new DateOnly(2027, 1, 1));
        await client.PutAsJsonAsync(WarrantiesUrl(itemId, warranty.Id),
            new { provider = "Bosch", expiresOn = "2028-03-12" }, CT);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(WarrantiesUrl(itemId, warranty.Id), CT)).StatusCode);

        var feedResp = await client.GetAsync($"/api/v1/inventory/items/{itemId}/activity", CT);
        Assert.Equal(HttpStatusCode.OK, feedResp.StatusCode);
        var feed = (await feedResp.Content.ReadFromJsonAsync<PaginatedResult<ActivityRow>>(CT))!;

        // Unlike notes, a warranty's audit rows ARE feed rows — they are the only trace of it.
        var summaries = feed.Items.Select(r => r.Summary!).ToList();
        Assert.Contains(summaries, s => s.EndsWith("added a warranty"));
        Assert.Contains(summaries, s => s.EndsWith("updated a warranty (expiry)"));
        Assert.Contains(summaries, s => s.EndsWith("removed a warranty (Bosch · until 2028-03-12)"));
        Assert.All(feed.Items, r => Assert.Equal("audit_log", r.Type));
    }

    // ----- Auth matrix (Contributor gate on writes; ActiveUser admits Viewer on reads) -----

    [Fact]
    public async Task Mutations_AsViewer_Return403()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        var warranty = await CreateWarrantyAsync(admin, itemId, provider: "Bosch");
        var viewer = await CreateViewerClientAsync();

        var post = await viewer.PostAsJsonAsync(WarrantiesUrl(itemId), new { }, CT);
        var put = await viewer.PutAsJsonAsync(WarrantiesUrl(itemId, warranty.Id), new { }, CT);
        var del = await viewer.DeleteAsync(WarrantiesUrl(itemId, warranty.Id), CT);

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
    }

    [Fact]
    public async Task GetWarranties_AsViewer_Returns200()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        await CreateWarrantyAsync(admin, itemId, provider: "Bosch");
        var viewer = await CreateViewerClientAsync();

        var resp = await viewer.GetAsync(WarrantiesUrl(itemId), CT);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Warranties_Anonymous_Returns401()
    {
        var anonymous = CreateAnonymousClient();

        var get = await anonymous.GetAsync(WarrantiesUrl(Guid.NewGuid()), CT);
        var post = await anonymous.PostAsJsonAsync(WarrantiesUrl(Guid.NewGuid()), new { }, CT);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task CreateWarranty_AsInactiveContributor_Returns403()
    {
        var admin = await CreateAdminClientAsync();
        var itemId = await CreateDurableAsync(admin);
        var inactive = await CreateUserClientAsync(isActive: false);

        var resp = await inactive.PostAsJsonAsync(WarrantiesUrl(itemId), new { }, CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
