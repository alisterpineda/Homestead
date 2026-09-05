using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Inventory;

// Mirrors the SearchResponse / SearchGroup / SearchHit contract returned by GET /api/v1/search.
internal record SearchResponseDto(string Query, IReadOnlyList<SearchGroupDto> Groups);
internal record SearchGroupDto(string Source, string Label, int Total, IReadOnlyList<SearchHitDto> Results);
internal record SearchHitDto(string Source, string Type, string Id, string Title, string? Subtitle, string? Badge);

[Collection("Integration")]
public class SearchEndpointTests : IntegrationTestBase
{
    public SearchEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ----- Helpers -----

    private static async Task<SearchResponseDto> SearchAsync(HttpClient client, string q, int? limit = null)
    {
        var url = $"/api/v1/search?q={Uri.EscapeDataString(q)}";
        if (limit is { } l)
            url += $"&limit={l}";

        var resp = await client.GetAsync(url, CT);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SearchResponseDto>(CT))!;
    }

    private static SearchGroupDto? Inventory(SearchResponseDto r) =>
        r.Groups.FirstOrDefault(g => g.Source == "inventory");

    private static async Task<DurableItem> CreateDurableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/durables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<DurableItem>(CT))!;
    }

    private static async Task<ConsumableItem> CreateConsumableAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/consumables", body, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ConsumableItem>(CT))!;
    }

    private static async Task<Guid> CreateAreaAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/inventory/areas", new { name }, CT);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AreaItem>(CT))!.Id;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid consumableId, object body) =>
        client.PostAsJsonAsync($"/api/v1/inventory/consumables/{consumableId}/receive", body, CT);

    // ----- Substring matches: one per searchable field -----

    [Fact]
    public async Task Search_MatchesName_Substring()
    {
        var client = await CreateAdminClientAsync();
        var drill = await CreateDurableAsync(client, new { name = "Cordless drill" });

        var group = Inventory(await SearchAsync(client, "cordless"));

        Assert.NotNull(group);
        var hit = Assert.Single(group!.Results);
        Assert.Equal(drill.Id.ToString(), hit.Id);
        Assert.Equal("Cordless drill", hit.Title);
    }

    [Fact]
    public async Task Search_MatchesSerial_Substring()
    {
        var client = await CreateAdminClientAsync();
        var hammer = await CreateDurableAsync(client, new { name = "Hammer", serial = "QWE-12345" });

        var group = Inventory(await SearchAsync(client, "qwe-123"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == hammer.Id.ToString());
    }

    [Fact]
    public async Task Search_MatchesUnit_Substring()
    {
        var client = await CreateAdminClientAsync();
        var tape = await CreateConsumableAsync(client, new { name = "Tape", unit = "metres" });

        var group = Inventory(await SearchAsync(client, "metr"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == tape.Id.ToString());
    }

    [Fact]
    public async Task Search_MatchesCompartment_Substring()
    {
        var client = await CreateAdminClientAsync();
        var areaId = await CreateAreaAsync(client, "Garage");
        var chest = await CreateDurableAsync(client, new { name = "Tool chest", areaId, isContainer = true });
        var wrench = await CreateDurableAsync(client,
            new { name = "Wrench", parentItemId = chest.Id, compartment = "Top shelf zone" });

        var group = Inventory(await SearchAsync(client, "shelf zone"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == wrench.Id.ToString());
    }

    [Fact]
    public async Task Search_MatchesNotes_Substring()
    {
        var client = await CreateAdminClientAsync();
        var lamp = await CreateDurableAsync(client, new { name = "Lamp", notes = "purchased at IKEA store" });

        var group = Inventory(await SearchAsync(client, "ikea"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == lamp.Id.ToString());
    }

    // ----- Fuzzy fallback (name only) -----

    [Fact]
    public async Task Search_FuzzyMatchesMisspelledName()
    {
        var client = await CreateAdminClientAsync();
        var battery = await CreateDurableAsync(client, new { name = "Battery" });

        // ILIKE "%battry%" can't match "Battery"; the word_similarity fallback does.
        var group = Inventory(await SearchAsync(client, "battry"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == battery.Id.ToString());
    }

    [Fact]
    public async Task Search_FuzzyMatchesPluralMismatch()
    {
        var client = await CreateAdminClientAsync();
        var box = await CreateDurableAsync(client, new { name = "Box" });

        var group = Inventory(await SearchAsync(client, "boxs"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == box.Id.ToString());
    }

    // ----- Exclusions, capping, guards -----

    [Fact]
    public async Task Search_ExcludesTerminalItems()
    {
        var client = await CreateAdminClientAsync();
        var live = await CreateDurableAsync(client, new { name = "Active server" });
        var archived = await CreateDurableAsync(client, new { name = "Decommissioned server" });
        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{archived.Id}",
            new[] { new { op = "replace", path = "/status", value = (object)"Archived" } });

        var group = Inventory(await SearchAsync(client, "server"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == live.Id.ToString());
        Assert.DoesNotContain(group.Results, h => h.Id == archived.Id.ToString());
    }

    [Fact]
    public async Task Search_CapsResultsToLimitButTotalReflectsFullCount()
    {
        var client = await CreateAdminClientAsync();
        for (var i = 1; i <= 8; i++)
            await CreateDurableAsync(client, new { name = $"Gizmo {i}" });

        var group = Inventory(await SearchAsync(client, "gizmo", limit: 3));

        Assert.NotNull(group);
        Assert.Equal(3, group!.Results.Count);
        Assert.Equal(8, group.Total);
    }

    [Fact]
    public async Task Search_BelowMinLength_ReturnsNoGroups()
    {
        var client = await CreateAdminClientAsync();
        await CreateDurableAsync(client, new { name = "Anything" });

        var result = await SearchAsync(client, "a");

        Assert.Empty(result.Groups);
    }

    // ----- Subtitle / type correctness -----

    [Fact]
    public async Task Search_DurableHit_SubtitleIsDirectLocation()
    {
        var client = await CreateAdminClientAsync();
        var garageId = await CreateAreaAsync(client, "Garage");
        var drill = await CreateDurableAsync(client, new { name = "Drill", areaId = garageId });

        var group = Inventory(await SearchAsync(client, "drill"));

        var hit = Assert.Single(group!.Results);
        Assert.Equal("durable", hit.Type);
        Assert.Equal("Garage", hit.Subtitle);
        Assert.Equal(drill.Id.ToString(), hit.Id);
    }

    [Fact]
    public async Task Search_UnplacedDurableHit_SubtitleIsUnplaced()
    {
        var client = await CreateAdminClientAsync();
        await CreateDurableAsync(client, new { name = "Floating widget" });

        var group = Inventory(await SearchAsync(client, "floating widget"));

        var hit = Assert.Single(group!.Results);
        Assert.Equal("Unplaced", hit.Subtitle);
    }

    [Fact]
    public async Task Search_ConsumableHit_SubtitleIsOnHandWithUnit()
    {
        var client = await CreateAdminClientAsync();
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var towels = await CreateConsumableAsync(client, new
        {
            name = "Paper towels",
            unit = "rolls",
            initialAreaId = pantryId,
            initialQuantity = 12m,
        });

        var group = Inventory(await SearchAsync(client, "paper"));

        var hit = Assert.Single(group!.Results);
        Assert.Equal("consumable", hit.Type);
        Assert.Equal("12 rolls", hit.Subtitle);
        Assert.Equal(towels.Id.ToString(), hit.Id);
    }

    // ----- Badges -----

    [Fact]
    public async Task Search_LentDurable_HasStatusBadge()
    {
        var client = await CreateAdminClientAsync();
        var ladder = await CreateDurableAsync(client, new { name = "Ladder" });
        await SendJsonPatchAsync(client,
            $"/api/v1/inventory/durables/{ladder.Id}",
            new object[]
            {
                new { op = "replace", path = "/status", value = (object)"Lent" },
                new { op = "replace", path = "/statusNote", value = (object)"Dave next door" },
            });

        var group = Inventory(await SearchAsync(client, "ladder"));

        var hit = Assert.Single(group!.Results);
        Assert.Equal("Lent", hit.Badge);
    }

    [Fact]
    public async Task Search_ConsumableBadge_ReflectsRestockThreshold()
    {
        var client = await CreateAdminClientAsync();
        var pantryId = await CreateAreaAsync(client, "Pantry");
        var low = await CreateConsumableAsync(client, new { name = "Screws low", unit = "screws", restockThreshold = 5m });
        var ample = await CreateConsumableAsync(client, new { name = "Screws ample", unit = "screws", restockThreshold = 5m });

        // The received quantities must persist for the badges to differ: 'low' lands at/below the
        // threshold (Low) while 'ample' lands above it (no badge). If receive were a no-op both would
        // sit at 0 on-hand and both would read "Low" — so the ample assertion makes receive load-bearing.
        await ReceiveAsync(client, low.Id, new { areaId = pantryId, quantity = 3m });
        await ReceiveAsync(client, ample.Id, new { areaId = pantryId, quantity = 20m });

        var group = Inventory(await SearchAsync(client, "screws"));

        Assert.NotNull(group);
        var lowHit = Assert.Single(group!.Results, h => h.Id == low.Id.ToString());
        var ampleHit = Assert.Single(group.Results, h => h.Id == ample.Id.ToString());
        Assert.Equal("Low", lowHit.Badge);
        Assert.Null(ampleHit.Badge);
    }

    // ----- ILIKE wildcard escaping (B4) -----

    [Fact]
    public async Task Search_EscapesLikeWildcard_MatchesLiteralPercentOnly()
    {
        var client = await CreateAdminClientAsync();

        // The "%" lives in a non-fuzzy field (Unit) and the names are dissimilar to the query, so the
        // Name-only word_similarity fallback stays out of it and this isolates the ILIKE path. Pre-fix,
        // the unescaped "%" widened the pattern to "%50%%" and matched the "500" unit too; escaped, "50%"
        // is a literal substring that matches only the "50%" unit.
        var literal = await CreateConsumableAsync(client, new { name = "Alpha tape", unit = "50%" });
        var other = await CreateConsumableAsync(client, new { name = "Beta tape", unit = "500" });

        var group = Inventory(await SearchAsync(client, "50%"));

        Assert.NotNull(group);
        Assert.Contains(group!.Results, h => h.Id == literal.Id.ToString());
        Assert.DoesNotContain(group.Results, h => h.Id == other.Id.ToString());
    }

    // ----- Authorization -----

    [Fact]
    public async Task Search_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();

        var resp = await client.GetAsync("/api/v1/search?q=drill", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Search_InactiveUser_Returns403()
    {
        var client = await CreateUserClientAsync(isActive: false);

        var resp = await client.GetAsync("/api/v1/search?q=drill", CT);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
