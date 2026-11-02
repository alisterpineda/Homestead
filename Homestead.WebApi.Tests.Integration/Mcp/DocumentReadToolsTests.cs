using System.Text.Json;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// The read tools (<c>search_documents</c>, <c>get_document</c>, <c>list_tags</c>, <c>list_parties</c>, and
/// <c>whoami</c>'s counts) end to end through a real MCP client. Documents are seeded by an Admin over the cookie
/// door; the MCP user is a separate bearer account, which sees every document outside a group.
/// </summary>
[Collection("Integration")]
public class DocumentReadToolsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // --- search_documents ---

    [Fact]
    public async Task Search_ByText_MatchesExtractedContent()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "Glacier letter.pdf", extractedText: "Notice about the zephyrine meter reading.");
        await SeedDocumentAsync(admin, "Other letter.pdf", extractedText: "Nothing relevant here.");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["query"] = "zephyrine" });

        Assert.Equal(["Glacier letter.pdf"], result.Items.Select(i => i.Name));
    }

    // Snippet and summary are derived from the file's content, so they travel under the untrusted_ label too. Asserted
    // on the raw JSON: the key is the contract the model reads.
    [Fact]
    public async Task Search_WithSummary_LabelsSnippetAndSummaryAsUntrusted()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "Glacier letter.pdf",
            extractedText: "Notice about the zephyrine meter reading.", summary: "A meter reading notice.");
        await using var client = await ConnectAsync(AppRole.Viewer);

        // CallAsync<JsonElement> keeps the wire names: the key is the contract the model reads.
        var json = await McpToolResults.CallAsync<JsonElement>(client, "search_documents",
            new Dictionary<string, object?> { ["query"] = "zephyrine", ["include_summary"] = true }, CT);

        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(["untrusted_snippet", "untrusted_summary"], ContentKeys(item));
        var snippet = item.GetProperty("untrusted_snippet").GetString();
        Assert.Equal("A meter reading notice.", item.GetProperty("untrusted_summary").GetString());
        Assert.Contains("zephyrine", snippet);
        Assert.DoesNotContain("<mark>", snippet);
    }

    [Fact]
    public async Task Search_ByTagPath_SnapsCasing_ExpandsChildren_ReturnsStoredPaths()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "Power bill.pdf", tagPaths: ["Home/Utilities/Electricity"]);
        await SeedDocumentAsync(admin, "Policy.pdf", tagPaths: ["Home/Insurance"]);
        await SeedDocumentAsync(admin, "Untagged.pdf");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["tag_paths"] = new[] { "home/utilities" } });

        var item = Assert.Single(result.Items);
        Assert.Equal(("Power bill.pdf", "Home/Utilities/Electricity"), (item.Name, Csv(item.Tags)));
    }

    [Fact]
    public async Task Search_UnknownTagPath_ReturnsSnapperErrorWithNearest()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Home/Utilities");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents",
            new Dictionary<string, object?> { ["tag_paths"] = new[] { "Home/Utilitis" } }, CT);

        Assert.Equal(
            "Unknown tag path(s): Home/Utilitis — nearest: Home/Utilities. " +
            "Use list_tags; call create_tag only if the user asked for a new tag.",
            error);
    }

    [Fact]
    public async Task Search_ByPartyName_MatchesCaseInsensitively_ReturnsStoredName()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "Acme bill.pdf", partyNames: ["Acme Power"]);
        await SeedDocumentAsync(admin, "Other bill.pdf", partyNames: ["Other Co"]);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["party_names"] = new[] { "acme power" } });

        var item = Assert.Single(result.Items);
        Assert.Equal(("Acme bill.pdf", "Acme Power"), (item.Name, Csv(item.Parties)));
    }

    [Fact]
    public async Task Search_ByDocumentDateRange_IsInclusive()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "before.pdf", documentDate: new DateOnly(2023, 12, 31));
        await SeedDocumentAsync(admin, "first-day.pdf", documentDate: new DateOnly(2024, 1, 1));
        await SeedDocumentAsync(admin, "last-day.pdf", documentDate: new DateOnly(2024, 12, 31));
        await SeedDocumentAsync(admin, "after.pdf", documentDate: new DateOnly(2025, 1, 1));
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new()
        {
            ["document_date_from"] = "2024-01-01",
            ["document_date_to"] = "2024-12-31",
        });

        Assert.Equal(["first-day.pdf", "last-day.pdf"], Names(result));
    }

    [Fact]
    public async Task Search_ByExpiryRange_ReturnsWhatExpiresInTheWindow()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "expires-soon.pdf", expiryDate: new DateOnly(2026, 12, 1));
        await SeedDocumentAsync(admin, "expires-later.pdf", expiryDate: new DateOnly(2027, 6, 1));
        await SeedDocumentAsync(admin, "never-expires.pdf");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new()
        {
            ["expiry_from"] = "2026-11-02",
            ["expiry_to"] = "2027-01-31",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(("expires-soon.pdf", new DateOnly(2026, 12, 1)), (item.Name, item.ExpiryDate));
    }

    [Fact]
    public async Task Search_HasTagsFalse_ReturnsOnlyUntagged()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "tagged.pdf", tagPaths: ["Home/Insurance"]);
        await SeedDocumentAsync(admin, "untagged.pdf");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["has_tags"] = false });

        Assert.Equal(["untagged.pdf"], Names(result));
    }

    [Fact]
    public async Task Search_PendingReviewTrue_ReturnsOnlyDocumentsAwaitingReview()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "awaiting.pdf", pendingNameSuggestion: true);
        await SeedDocumentAsync(admin, "reviewed.pdf");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["pending_review"] = true });

        var item = Assert.Single(result.Items);
        Assert.Equal(("awaiting.pdf", true), (item.Name, item.PendingReview));
    }

    [Fact]
    public async Task Search_InTrash_ShowsTrashedDocument_DefaultHidesIt()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "kept.pdf");
        var trashed = await SeedDocumentAsync(admin, "trashed.pdf");
        (await admin.DeleteAsync($"/api/v1/documents/{trashed}", CT)).EnsureSuccessStatusCode();
        await using var client = await ConnectAsync(AppRole.Viewer);

        var active = await SearchAsync(client, new());
        var trash = await SearchAsync(client, new() { ["in_trash"] = true });

        Assert.Equal(
            ("kept.pdf", "trashed.pdf", true),
            (Csv(Names(active)), Csv(Names(trash)), trash.Items.All(i => i.DeletedAt is not null)));
    }

    [Fact]
    public async Task Search_PageSizeAbove100_IsClamped()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["page_size"] = 500 });

        Assert.Equal(100, result.PageSize);
    }

    // --- group access ---

    [Fact]
    public async Task Search_ContributorOutsideGroup_DoesNotSeeTheGroupsDocument()
    {
        var admin = await CreateAdminClientAsync();
        var group = await CreateGroupAsync(admin, "MCP Private Group");
        await SeedDocumentAsync(admin, "group-secret.pdf", groupId: group);
        await SeedDocumentAsync(admin, "household.pdf");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await SearchAsync(client, new());

        Assert.Equal(["household.pdf"], Names(result));
    }

    [Fact]
    public async Task GetDocument_ContributorOutsideGroup_IsNotFoundOrNoAccess()
    {
        var admin = await CreateAdminClientAsync();
        var group = await CreateGroupAsync(admin, "MCP Private Group");
        var secret = await SeedDocumentAsync(admin, "group-secret.pdf", groupId: group);
        await using var client = await ConnectAsync(AppRole.Contributor);

        var error = await McpToolResults.CallForErrorAsync(client, "get_document",
            new Dictionary<string, object?> { ["document_id"] = secret }, CT);

        Assert.StartsWith("Not found or no access", error);
    }

    // --- get_document ---

    [Fact]
    public async Task GetDocument_IncludeText_ReadsLongTextInTwoChunks()
    {
        var admin = await CreateAdminClientAsync();
        var text = new string('a', 1000) + new string('b', 500);
        var id = await SeedDocumentAsync(admin, "long.pdf", extractedText: text);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var first = await GetDocumentAsync(client, id, new() { ["include_text"] = true, ["text_max_chars"] = 1000 });
        var second = await GetDocumentAsync(client, id, new()
        {
            ["include_text"] = true, ["text_max_chars"] = 1000, ["text_offset"] = first.NextOffset,
        });

        Assert.Equal(
            (First: (new string('a', 1000), 1500, (int?)1000), Second: (new string('b', 500), 1500, (int?)null)),
            (First: (first.UntrustedDocumentText, first.TextTotalChars, first.NextOffset),
             Second: (second.UntrustedDocumentText, second.TextTotalChars, second.NextOffset)));
    }

    [Fact]
    public async Task GetDocument_LabelsSummaryAsUntrusted()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "letter.pdf", summary: "A meter reading notice.");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var json = await McpToolResults.CallAsync<JsonElement>(client, "get_document",
            new Dictionary<string, object?> { ["document_id"] = id }, CT);

        Assert.Equal(["untrusted_summary"], ContentKeys(json));
        Assert.Equal("A meter reading notice.", json.GetProperty("untrusted_summary").GetString());
    }

    [Fact]
    public async Task GetDocument_WithoutIncludeText_ReportsOnlyTheTextLength()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "long.pdf", extractedText: new string('x', 1500));
        await using var client = await ConnectAsync(AppRole.Viewer);

        var document = await GetDocumentAsync(client, id, new());

        Assert.Equal(((string?)null, 1500, (int?)null),
            (document.UntrustedDocumentText, document.TextTotalChars, document.NextOffset));
    }

    [Fact]
    public async Task GetDocument_PendingTagAndPartySuggestions_ShowPathAndName_NotIds()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "policy.pdf");
        var tagId = await CreateTagAsync(admin, "Home/Insurance");
        var partyId = await CreatePartyAsync(admin, "Acme Insurance");
        await AddPendingSuggestionAsync(id, DocumentSuggestionField.Tags, tagId.ToString());
        await AddPendingSuggestionAsync(id, DocumentSuggestionField.Parties, partyId.ToString());
        await using var client = await ConnectAsync(AppRole.Viewer);

        var document = await GetDocumentAsync(client, id, new());

        Assert.Equal(
            [("Parties", "Acme Insurance"), ("Tags", "Home/Insurance")],
            document.PendingSuggestions.Select(s => (s.Field, s.SuggestedValue)).Order());
    }

    // --- list_tags / list_parties ---

    [Fact]
    public async Task ListTags_SecondPage_ReturnsTheRemainderWithPageInfo()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Alpha");
        await CreateTagAsync(admin, "Bravo");
        await CreateTagAsync(admin, "Charlie");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await McpToolResults.CallAsync<ListTagsPayload>(client, "list_tags",
            new Dictionary<string, object?> { ["page"] = 2, ["page_size"] = 2 }, CT);

        Assert.Equal(
            ("Charlie", 2, 2, 3, 2),
            (Csv(result.Items.Select(t => t.Path)), result.Page, result.PageSize, result.TotalCount, result.TotalPages));
    }

    [Fact]
    public async Task ListParties_SecondPage_ReturnsTheRemainderWithPageInfo()
    {
        var admin = await CreateAdminClientAsync();
        await CreatePartyAsync(admin, "Alpha Co");
        await CreatePartyAsync(admin, "Bravo Co");
        await CreatePartyAsync(admin, "Charlie Co");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await McpToolResults.CallAsync<ListPartiesPayload>(client, "list_parties",
            new Dictionary<string, object?> { ["page"] = 2, ["page_size"] = 2 }, CT);

        Assert.Equal(
            ("Charlie Co", 2, 2, 3, 2),
            (Csv(result.Items.Select(p => p.Name)), result.Page, result.PageSize, result.TotalCount, result.TotalPages));
    }

    // --- read-only roles ---

    // A Guest sees only its groups' documents, so the document lives in a group both roles belong to.
    [Theory]
    [InlineData(AppRole.Viewer)]
    [InlineData(AppRole.Guest)]
    public async Task ReadOnlyRole_CanCallAllFourReadTools(AppRole role)
    {
        var admin = await CreateAdminClientAsync();
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, role);
        var group = await CreateGroupAsync(admin, $"MCP {role} Group");
        await AddGroupMemberAsync(admin, group, user.Id, accessLevel: "Viewer");
        var id = await SeedDocumentAsync(admin, "shared.pdf", tagPaths: ["Home/Insurance"], partyNames: ["Acme Insurance"],
            groupId: group);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var search = await SearchAsync(client, new());
        var document = await GetDocumentAsync(client, id, new());
        var tags = await McpToolResults.CallAsync<ListTagsPayload>(client, "list_tags", null, CT);
        var parties = await McpToolResults.CallAsync<ListPartiesPayload>(client, "list_parties", null, CT);

        Assert.Equal(
            (id.ToString(), "shared.pdf", true, "Acme Insurance"),
            (Csv(search.Items.Select(i => i.Id)), document.Name, tags.Items.Any(t => t.Path == "Home/Insurance"),
             Csv(parties.Items.Select(p => p.Name))));
    }

    // --- whoami counts ---

    [Fact]
    public async Task WhoAmI_CountsUntaggedAndPendingReviewDocuments()
    {
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "untagged-awaiting.pdf", pendingNameSuggestion: true);
        await SeedDocumentAsync(admin, "untagged.pdf");
        await SeedDocumentAsync(admin, "tagged.pdf", tagPaths: ["Home/Insurance"]);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var whoAmI = await McpToolResults.CallAsync<WhoAmICountsPayload>(client, "whoami", null, CT);

        Assert.Equal(new WhoAmICountsPayload(UntaggedCount: 2, PendingReviewCount: 1), whoAmI);
    }

    // --- helpers ---

    private async Task<McpClient> ConnectAsync(AppRole role)
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, role);
        return await McpClientHelper.ConnectAsync(Factory, token, CT);
    }

    private static Task<SearchDocumentsPayload> SearchAsync(McpClient client, Dictionary<string, object?> arguments) =>
        McpToolResults.CallAsync<SearchDocumentsPayload>(client, "search_documents", arguments, CT);

    private static Task<GetDocumentPayload> GetDocumentAsync(
        McpClient client, Guid id, Dictionary<string, object?> arguments)
    {
        arguments["document_id"] = id;
        return McpToolResults.CallAsync<GetDocumentPayload>(client, "get_document", arguments, CT);
    }

    // Lists compared inside a tuple must be values (xUnit compares arrays in a tuple by reference).
    private static string Csv<T>(IEnumerable<T> values) => string.Join(", ", values);

    // Which of the file-derived snippet/summary keys, labelled or not, an object carries.
    private static IReadOnlyList<string> ContentKeys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name)
            .Where(n => n is "snippet" or "summary" or "untrusted_snippet" or "untrusted_summary")
            .Order(StringComparer.Ordinal).ToList();

    private static IReadOnlyList<string> Names(SearchDocumentsPayload result) =>
        result.Items.Select(i => i.Name).Order(StringComparer.Ordinal).ToList();

    // Uploads through the cookie door, then sets tags/parties the way the UI editor does (one replace each), and
    // dates, text and a pending suggestion directly — both dates always, so no upload-time default leaks in.
    private async Task<Guid> SeedDocumentAsync(
        HttpClient admin,
        string name,
        string[]? tagPaths = null,
        string[]? partyNames = null,
        DateOnly? documentDate = null,
        DateOnly? expiryDate = null,
        bool pendingNameSuggestion = false,
        Guid? groupId = null,
        string? extractedText = null,
        string? summary = null)
    {
        var id = await UploadAndWaitAsync(admin, name, groupId);

        var ops = new List<object>();
        if (tagPaths is not null)
            ops.Add(new { op = "replace", path = "/tagPaths", value = (object)tagPaths });
        if (partyNames is not null)
        {
            var partyIds = new List<Guid>();
            foreach (var partyName in partyNames)
                partyIds.Add(await CreatePartyAsync(admin, partyName));
            ops.Add(new { op = "replace", path = "/partyIds", value = (object)partyIds });
        }
        if (ops.Count > 0)
            (await SendJsonPatchAsync(admin, $"/api/v1/documents/{id}", ops)).EnsureSuccessStatusCode();

        if (extractedText is not null)
            await SetExtractedTextAsync(id, extractedText);

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents.FirstAsync(d => d.Id == id, CT);
        document.DocumentDate = documentDate;
        document.ExpiryDate = expiryDate;
        if (summary is not null)
            document.Summary = summary;
        await db.SaveChangesAsync(CT);

        if (pendingNameSuggestion)
            await AddPendingSuggestionAsync(id, DocumentSuggestionField.Name, "Suggested " + name);

        return id;
    }

    private async Task AddPendingSuggestionAsync(Guid documentId, DocumentSuggestionField field, string suggestedValue)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = documentId,
            Field = field,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggestedValue,
            IsApplied = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CT);
    }

    private readonly Dictionary<string, Guid> _parties = new(StringComparer.Ordinal);

    private async Task<Guid> CreatePartyAsync(HttpClient admin, string name)
    {
        if (_parties.TryGetValue(name, out var existing))
            return existing;

        var response = await admin.PostAsJsonAsync("/api/v1/documents/parties", new { name }, CT);
        response.EnsureSuccessStatusCode();
        var party = await response.Content.ReadFromJsonAsync<CreatedEntity>(CT);
        return _parties[name] = party!.Id;
    }

    private async Task<Guid> CreateTagAsync(HttpClient admin, string path)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/documents/tags", new { path }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedEntity>(CT))!.Id;
    }

    private sealed record CreatedEntity(Guid Id);
}
