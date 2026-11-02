using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Documents;

/// <summary>
/// The document list's date-range, has-tags / has-parties / has-pending-suggestions filters and the
/// <c>ExpiryDate</c> sort (GET /api/v1/documents).
/// </summary>
[Collection("Integration")]
public class DocumentSearchFilterTests : IntegrationTestBase
{
    public DocumentSearchFilterTests(PostgresFixture fixture) : base(fixture) { }

    private const string ListUrl = "/api/v1/documents?Page=1&PageSize=100&IncludeChildTags=false";

    // --- DocumentDate range ---

    [Fact]
    public async Task DocumentDateRange_IncludesBothBoundaryDays_ExcludesOutsideAndUndated()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "before.pdf", documentDate: new DateOnly(2023, 12, 31));
        await SeedAsync(client, "from-day.pdf", documentDate: new DateOnly(2024, 1, 1));
        await SeedAsync(client, "middle.pdf", documentDate: new DateOnly(2024, 6, 15));
        await SeedAsync(client, "to-day.pdf", documentDate: new DateOnly(2024, 12, 31));
        await SeedAsync(client, "after.pdf", documentDate: new DateOnly(2025, 1, 1));
        await SeedAsync(client, "undated.pdf");

        var names = await GetNamesAsync(client, "&DocumentDateFrom=2024-01-01&DocumentDateTo=2024-12-31");

        Assert.Equal(["from-day.pdf", "middle.pdf", "to-day.pdf"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DocumentDateFromOnly_IsOpenEndedUpward()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "old.pdf", documentDate: new DateOnly(2020, 5, 1));
        await SeedAsync(client, "exact.pdf", documentDate: new DateOnly(2024, 3, 10));
        await SeedAsync(client, "new.pdf", documentDate: new DateOnly(2030, 1, 1));
        await SeedAsync(client, "undated.pdf");

        var names = await GetNamesAsync(client, "&DocumentDateFrom=2024-03-10");

        Assert.Equal(["exact.pdf", "new.pdf"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DocumentDateSameDayRange_ReturnsThatDayOnly()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "day-before.pdf", documentDate: new DateOnly(2024, 7, 3));
        await SeedAsync(client, "the-day.pdf", documentDate: new DateOnly(2024, 7, 4));
        await SeedAsync(client, "day-after.pdf", documentDate: new DateOnly(2024, 7, 5));

        var names = await GetNamesAsync(client, "&DocumentDateFrom=2024-07-04&DocumentDateTo=2024-07-04");

        Assert.Equal(["the-day.pdf"], names);
    }

    [Fact]
    public async Task DocumentDateFromAfterTo_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"{ListUrl}&DocumentDateFrom=2024-02-01&DocumentDateTo=2024-01-31", CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- ExpiryDate range ---

    [Fact]
    public async Task ExpiryDateRange_IncludesBothBoundaryDays_ExcludesOutsideAndNoExpiry()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "expired-earlier.pdf", expiryDate: new DateOnly(2026, 10, 31));
        await SeedAsync(client, "expires-from.pdf", expiryDate: new DateOnly(2026, 11, 1));
        await SeedAsync(client, "expires-to.pdf", expiryDate: new DateOnly(2027, 1, 30));
        await SeedAsync(client, "expires-later.pdf", expiryDate: new DateOnly(2027, 1, 31));
        await SeedAsync(client, "never-expires.pdf");

        var names = await GetNamesAsync(client, "&ExpiryDateFrom=2026-11-01&ExpiryDateTo=2027-01-30");

        Assert.Equal(["expires-from.pdf", "expires-to.pdf"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExpiryDateToOnly_IsOpenEndedDownward_ExcludesNoExpiry()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "long-gone.pdf", expiryDate: new DateOnly(2001, 1, 1));
        await SeedAsync(client, "on-cutoff.pdf", expiryDate: new DateOnly(2026, 12, 31));
        await SeedAsync(client, "future.pdf", expiryDate: new DateOnly(2027, 1, 1));
        await SeedAsync(client, "never-expires.pdf");

        var names = await GetNamesAsync(client, "&ExpiryDateTo=2026-12-31");

        Assert.Equal(["long-gone.pdf", "on-cutoff.pdf"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExpiryDateFromAfterTo_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync($"{ListUrl}&ExpiryDateFrom=2027-01-02&ExpiryDateTo=2027-01-01", CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- HasTags / HasParties / HasPendingSuggestions ---

    [Fact]
    public async Task HasTagsFalse_ReturnsOnlyUntagged()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "tagged.pdf", tagPath: "Filters/Tagged");
        await SeedAsync(client, "untagged.pdf");

        var names = await GetNamesAsync(client, "&HasTags=false");

        Assert.Equal(["untagged.pdf"], names);
    }

    [Fact]
    public async Task HasTagsTrue_ReturnsOnlyTagged()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "tagged.pdf", tagPath: "Filters/Tagged");
        await SeedAsync(client, "untagged.pdf");

        var names = await GetNamesAsync(client, "&HasTags=true");

        Assert.Equal(["tagged.pdf"], names);
    }

    [Fact]
    public async Task HasPartiesFalse_ReturnsOnlyDocumentsWithoutParties()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "with-party.pdf", partyName: "Filter Party");
        await SeedAsync(client, "no-party.pdf");

        var names = await GetNamesAsync(client, "&HasParties=false");

        Assert.Equal(["no-party.pdf"], names);
    }

    [Fact]
    public async Task HasPartiesTrue_ReturnsOnlyDocumentsWithParties()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "with-party.pdf", partyName: "Filter Party");
        await SeedAsync(client, "no-party.pdf");

        var names = await GetNamesAsync(client, "&HasParties=true");

        Assert.Equal(["with-party.pdf"], names);
    }

    [Fact]
    public async Task HasPendingSuggestionsTrue_ReturnsOnlyDocumentsWithAPendingSuggestion()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "pending.pdf", suggestionStatus: DocumentSuggestionStatus.Pending);
        await SeedAsync(client, "declined-only.pdf", suggestionStatus: DocumentSuggestionStatus.Declined);
        await SeedAsync(client, "no-suggestions.pdf");

        var names = await GetNamesAsync(client, "&HasPendingSuggestions=true");

        Assert.Equal(["pending.pdf"], names);
    }

    [Fact]
    public async Task HasPendingSuggestionsFalse_IncludesResolvedOnlyAndNoSuggestionDocuments()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "pending.pdf", suggestionStatus: DocumentSuggestionStatus.Pending);
        await SeedAsync(client, "declined-only.pdf", suggestionStatus: DocumentSuggestionStatus.Declined);
        await SeedAsync(client, "no-suggestions.pdf");

        var names = await GetNamesAsync(client, "&HasPendingSuggestions=false");

        Assert.Equal(["declined-only.pdf", "no-suggestions.pdf"], names.Order(StringComparer.Ordinal));
    }

    // --- Composition ---

    [Fact]
    public async Task Filters_ComposeWithTextSearch()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "Zephyr invoice 2024.pdf", documentDate: new DateOnly(2024, 4, 1));
        await SeedAsync(client, "Zephyr invoice 2023.pdf", documentDate: new DateOnly(2023, 4, 1));
        await SeedAsync(client, "Zephyr invoice tagged.pdf", documentDate: new DateOnly(2024, 5, 1), tagPath: "Filters/Done");
        await SeedAsync(client, "Unrelated 2024.pdf", documentDate: new DateOnly(2024, 4, 1));

        var names = await GetNamesAsync(client,
            "&Search=zephyr&DocumentDateFrom=2024-01-01&DocumentDateTo=2024-12-31&HasTags=false");

        Assert.Equal(["Zephyr invoice 2024.pdf"], names);
    }

    [Fact]
    public async Task Filters_RunOnTheAccessFilteredQuery()
    {
        var adminClient = await CreateAdminClientAsync();
        var hiddenGroup = await CreateGroupAsync(adminClient, "SearchFilter Hidden Group");
        await SeedAsync(adminClient, "visible-untagged.pdf");
        await SeedAsync(adminClient, "hidden-untagged.pdf", groupId: hiddenGroup);

        var contributorClient = await CreateUserClientAsync(role: "Contributor");
        var names = await GetNamesAsync(contributorClient, "&HasTags=false");

        Assert.Equal(["visible-untagged.pdf"], names);
    }

    // --- ExpiryDate sort ---

    [Fact]
    public async Task SortByExpiryDateAsc_OrdersEarliestFirst_NoExpiryLast()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "none-1.pdf");
        await SeedAsync(client, "late.pdf", expiryDate: new DateOnly(2029, 1, 1));
        await SeedAsync(client, "early.pdf", expiryDate: new DateOnly(2027, 1, 1));
        await SeedAsync(client, "none-2.pdf");
        await SeedAsync(client, "mid.pdf", expiryDate: new DateOnly(2028, 1, 1));

        var names = await GetNamesAsync(client, "&SortBy=ExpiryDate&SortDir=Asc");

        Assert.Equal(["early.pdf", "mid.pdf", "late.pdf"], names.Take(3));
        Assert.Equal(["none-1.pdf", "none-2.pdf"], names.Skip(3).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SortByExpiryDateDesc_OrdersLatestFirst_NoExpiryLast()
    {
        var client = await CreateAdminClientAsync();
        await SeedAsync(client, "none-1.pdf");
        await SeedAsync(client, "late.pdf", expiryDate: new DateOnly(2029, 1, 1));
        await SeedAsync(client, "early.pdf", expiryDate: new DateOnly(2027, 1, 1));
        await SeedAsync(client, "none-2.pdf");
        await SeedAsync(client, "mid.pdf", expiryDate: new DateOnly(2028, 1, 1));

        var names = await GetNamesAsync(client, "&SortBy=ExpiryDate&SortDir=Desc");

        Assert.Equal(["late.pdf", "mid.pdf", "early.pdf"], names.Take(3));
        Assert.Equal(["none-1.pdf", "none-2.pdf"], names.Skip(3).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SortByExpiryDate_ExplicitSortWinsOverRankDuringSearch()
    {
        var client = await CreateAdminClientAsync();
        // Relevance (the denser name match) and the UpdatedAt tiebreaker (seeded last) both put the
        // triple-quokka document first; the explicit expiry sort must override them.
        await SeedAsync(client, "Quokka lease.pdf", expiryDate: new DateOnly(2027, 1, 1));
        await SeedAsync(client, "Quokka quokka quokka.pdf", expiryDate: new DateOnly(2030, 1, 1));

        var names = await GetNamesAsync(client, "&Search=quokka&SortBy=ExpiryDate&SortDir=Asc");

        Assert.Equal(["Quokka lease.pdf", "Quokka quokka quokka.pdf"], names);
    }

    // --- Helpers ---

    private async Task<IReadOnlyList<string>> GetNamesAsync(HttpClient client, string filterQuery)
    {
        var response = await client.GetAsync(ListUrl + filterQuery, CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<FilterListItem>>(CT);
        return body!.Items.Select(d => d.Name).ToList();
    }

    private async Task<Guid> SeedAsync(
        HttpClient client,
        string name,
        DateOnly? documentDate = null,
        DateOnly? expiryDate = null,
        string? tagPath = null,
        string? partyName = null,
        DocumentSuggestionStatus? suggestionStatus = null,
        Guid? groupId = null)
    {
        var docId = await UploadAndWaitAsync(client, name, groupId);

        if (tagPath is not null)
        {
            var patch = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
            {
                new { op = "replace", path = "/tagPaths", value = (object)new[] { tagPath } }
            });
            patch.EnsureSuccessStatusCode();
        }

        if (partyName is not null)
        {
            var partyId = await GetOrCreatePartyAsync(client, partyName);
            var patch = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
            {
                new { op = "replace", path = "/partyIds", value = (object)new[] { partyId } }
            });
            patch.EnsureSuccessStatusCode();
        }

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents.FirstAsync(d => d.Id == docId, CT);
        // Set both explicitly so an upload-time default can never leak into the assertions.
        document.DocumentDate = documentDate;
        document.ExpiryDate = expiryDate;

        if (suggestionStatus is { } status)
        {
            db.DocumentSuggestions.Add(new DocumentSuggestion
            {
                DocumentId = docId,
                Field = DocumentSuggestionField.Name,
                Status = status,
                SuggestedValue = "Suggested " + name,
                OriginalValue = name,
                IsApplied = false,
                CreatedAt = DateTimeOffset.UtcNow,
                ResolvedAt = status == DocumentSuggestionStatus.Pending ? null : DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(CT);
        return docId;
    }

    private readonly Dictionary<string, Guid> _parties = new(StringComparer.Ordinal);

    private async Task<Guid> GetOrCreatePartyAsync(HttpClient client, string name)
    {
        if (_parties.TryGetValue(name, out var id))
            return id;

        var response = await client.PostAsJsonAsync("/api/v1/documents/parties", new { name }, CT);
        response.EnsureSuccessStatusCode();
        var party = await response.Content.ReadFromJsonAsync<PartyResult>(CT);
        _parties[name] = party!.Id;
        return party.Id;
    }

    private record FilterListItem(Guid Id, string Name, DateOnly? DocumentDate, DateOnly? ExpiryDate);
    private record PartyResult(Guid Id, string Name);
}
