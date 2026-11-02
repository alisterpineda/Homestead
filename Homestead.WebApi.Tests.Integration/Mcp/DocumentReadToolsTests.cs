using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// The read tools (<c>search_documents</c>, <c>get_document</c>, <c>list_tags</c>, <c>list_parties</c>, and
/// <c>whoami</c>'s counts) end to end through a real MCP client. Documents are seeded by an Admin over the cookie
/// door; the MCP user is a separate bearer account, which sees every document outside a group.
/// </summary>
[Collection("Integration")]
public partial class DocumentReadToolsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
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

    // The trash branch of the summary read: a trashed document's summary is found under in_trash.
    [Fact]
    public async Task Search_InTrashWithSummary_ReturnsTheTrashedDocumentsSummary()
    {
        var admin = await CreateAdminClientAsync();
        var trashed = await SeedDocumentAsync(admin, "trashed.pdf", summary: "An old meter reading notice.");
        (await admin.DeleteAsync($"/api/v1/documents/{trashed}", CT)).EnsureSuccessStatusCode();
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["in_trash"] = true, ["include_summary"] = true });

        var item = Assert.Single(result.Items);
        Assert.Equal(("trashed.pdf", "An old meter reading notice."), (item.Name, item.UntrustedSummary));
    }

    [Fact]
    public async Task Search_SortByExpiryAscending_PutsTheDocumentWithoutAnExpiryLast()
    {
        // Seeded so that no order the tool could fall back to matches the expected one (soonest expiry, later expiry,
        // no expiry): not name, not seed order (= updated_at), not document_date, in either direction.
        var admin = await CreateAdminClientAsync();
        await SeedDocumentAsync(admin, "z-later.pdf",
            documentDate: new DateOnly(2024, 1, 1), expiryDate: new DateOnly(2027, 1, 1));
        await SeedDocumentAsync(admin, "a-none.pdf", documentDate: new DateOnly(2024, 2, 1));
        await SeedDocumentAsync(admin, "m-sooner.pdf",
            documentDate: new DateOnly(2024, 3, 1), expiryDate: new DateOnly(2026, 6, 1));
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await SearchAsync(client, new() { ["sort_by"] = "expiry_date", ["sort_dir"] = "asc" });

        Assert.Equal(["m-sooner.pdf", "z-later.pdf", "a-none.pdf"], result.Items.Select(i => i.Name));
    }

    // Bad input reaches the model as a recoverable "Invalid input: …" naming the parameter, not a generic SDK error.
    [Theory]
    [InlineData("document_date_from", "2024-3-7",
        "Invalid input: document_date_from must be a date in yyyy-MM-dd form, got '2024-3-7'.")]
    [InlineData("sort_by", "size",
        "Invalid input: sort_by must be name, document_date, expiry_date or updated_at, got 'size'.")]
    [InlineData("sort_dir", "sideways", "Invalid input: sort_dir must be asc or desc, got 'sideways'.")]
    public async Task Search_BadInput_IsAnInvalidInputError(string parameter, string value, string expected)
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents",
            new Dictionary<string, object?> { [parameter] = value }, CT);

        Assert.Equal(expected, error);
    }

    // Checked by the tool in wire names, as invalid input — not GetDocumentsHandler's own (C# property names) check.
    [Fact]
    public async Task Search_DocumentDateFromAfterTo_IsAnError()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents", new Dictionary<string, object?>
        {
            ["document_date_from"] = "2024-12-31", ["document_date_to"] = "2024-01-01",
        }, CT);

        Assert.Equal("Invalid input: document_date_from must be on or before document_date_to.", error);
    }

    [Fact]
    public async Task Search_ExpiryFromAfterTo_IsAnError()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents", new Dictionary<string, object?>
        {
            ["expiry_from"] = "2025-06-30", ["expiry_to"] = "2025-06-01",
        }, CT);

        Assert.Equal("Invalid input: expiry_from must be on or before expiry_to.", error);
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
    public async Task GetDocument_IncludeText_CountsCodePoints_SoAChunkAfterAnEmojiLosesNothing()
    {
        var admin = await CreateAdminClientAsync();
        // 998 + 1 + 600 = 1599 code points (1600 UTF-16 units); the first 1000-char chunk ends one 'b' past "🙂".
        var text = new string('a', 998) + "🙂" + new string('b', 600);
        var id = await SeedDocumentAsync(admin, "emoji.pdf", extractedText: text);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var first = await GetDocumentAsync(client, id, new() { ["include_text"] = true, ["text_max_chars"] = 1000 });
        var second = await GetDocumentAsync(client, id, new()
        {
            ["include_text"] = true, ["text_max_chars"] = 1000, ["text_offset"] = first.NextOffset,
        });

        Assert.Equal(
            (Text: text, Totals: (1599, 1599), Next: ((int?)1000, (int?)null)),
            (Text: first.UntrustedDocumentText + second.UntrustedDocumentText,
             Totals: (first.TextTotalChars, second.TextTotalChars),
             Next: (first.NextOffset, second.NextOffset)));
    }

    [Fact]
    public async Task GetDocument_WithoutIncludeText_NeverSelectsTheText_OnlyItsLength()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "long.pdf", extractedText: new string('x', 1500));
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Viewer);
        var sql = new SqlCapture();
        await using var host = await StartSqlCapturingHostAsync(sql);
        await using var client = await McpClientHelper.ConnectAsync(host.Factory, token, CT);

        sql.Clear();
        var withoutText = await GetDocumentAsync(client, id, new());
        var usesWithoutText = sql.TextColumnUses();
        sql.Clear();
        await GetDocumentAsync(client, id, new() { ["include_text"] = true });
        // The control: asking for text reads it, but only as a slice measured in the same statement — never the
        // bare column (the whole text).
        var usesWithText = sql.TextColumnUses();

        Assert.Equal((WithoutText: "length", WithText: "length | length+substr", Total: 1500),
            (WithoutText: usesWithoutText, WithText: usesWithText, Total: withoutText.TextTotalChars));
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

    [Theory]
    [InlineData(1500)]
    [InlineData(int.MaxValue)]
    public async Task GetDocument_IncludeText_OffsetAtOrPastTheEnd_ReturnsNoText_AndNoNextOffset(int offset)
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "long.pdf", extractedText: new string('x', 1500));
        await using var client = await ConnectAsync(AppRole.Viewer);

        var document = await GetDocumentAsync(client, id, new() { ["include_text"] = true, ["text_offset"] = offset });

        Assert.Equal(("", 1500, (int?)null),
            (document.UntrustedDocumentText, document.TextTotalChars, document.NextOffset));
    }

    [Fact]
    public async Task GetDocument_NullExtractedText_ReportsNoText_WithOrWithoutIncludeText()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "blank-scan.pdf");
        await SetExtractedTextAsync(id, null);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var without = await GetDocumentAsync(client, id, new());
        var with = await GetDocumentAsync(client, id, new() { ["include_text"] = true });

        Assert.Equal(
            (Without: ((string?)null, 0, (int?)null), With: (true, 0, (int?)null)),
            (Without: (without.UntrustedDocumentText, without.TextTotalChars, without.NextOffset),
             With: (string.IsNullOrEmpty(with.UntrustedDocumentText), with.TextTotalChars, with.NextOffset)));
    }

    // Everything get_document assembles, on a document whose second version differs from the first in extension,
    // size and text — so a projection that picked the first version, or dropped a field, shows.
    [Fact]
    public async Task GetDocument_ReturnsTheFullMetadata_AndTheLatestVersionsFileAndText()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Home/Insurance", description: "Policies and claims");
        await CreatePartyAsync(admin, "Acme Insurance", description: "The home insurer");
        var id = await SeedDocumentAsync(admin, "policy.pdf",
            tagPaths: ["Home/Insurance"], partyNames: ["Acme Insurance"],
            documentDate: new DateOnly(2024, 3, 15), expiryDate: new DateOnly(2025, 3, 14),
            extractedText: "The first version's text, which is longer than the second's.");
        await UploadPngVersionAndWaitAsync(admin, id, "policy-renewal.png");
        await SetExtractedTextAsync(id, "Renewal notice.");
        await SetNotesAsync(id, "Renewed by phone.");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var document = await GetDocumentAsync(client, id, new() { ["include_text"] = true });

        Assert.Equal(
            (Versions: 2, Extension: ".png", HasSize: true, Text: "Renewal notice.", Total: 15, Next: (int?)null,
             DocumentDate: (DateOnly?)new DateOnly(2024, 3, 15), ExpiryDate: (DateOnly?)new DateOnly(2025, 3, 14),
             Notes: "Renewed by phone.",
             Tags: "Home/Insurance: Policies and claims", Parties: "Acme Insurance: The home insurer"),
            (Versions: document.VersionCount, Extension: document.FileExtension, HasSize: document.FileSize > 0,
             Text: document.UntrustedDocumentText, Total: document.TextTotalChars, Next: document.NextOffset,
             DocumentDate: document.DocumentDate, ExpiryDate: document.ExpiryDate,
             Notes: document.Notes,
             Tags: Csv(document.Tags.Select(t => $"{t.Path}: {t.Description}")),
             Parties: Csv(document.Parties.Select(p => $"{p.Name}: {p.Description}"))));
    }

    [Fact]
    public async Task GetDocument_ListsOnlyPendingSuggestions_NotResolvedOnes()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "letter.pdf");
        await AddSuggestionAsync(id, DocumentSuggestionField.Name, "Pending name");
        await AddSuggestionAsync(id, DocumentSuggestionField.Name, "Declined name", DocumentSuggestionStatus.Declined);
        await AddSuggestionAsync(id, DocumentSuggestionField.DocumentDate, "2024-01-01", DocumentSuggestionStatus.Approved);
        await using var client = await ConnectAsync(AppRole.Viewer);

        var document = await GetDocumentAsync(client, id, new());

        Assert.Equal("Name: Pending name", Csv(document.PendingSuggestions.Select(s => $"{s.Field}: {s.SuggestedValue}")));
    }

    [Fact]
    public async Task GetDocument_PendingTagAndPartySuggestions_ShowPathAndName_NotIds()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedDocumentAsync(admin, "policy.pdf");
        var tagId = await CreateTagAsync(admin, "Home/Insurance");
        var partyId = await CreatePartyAsync(admin, "Acme Insurance");
        await AddSuggestionAsync(id, DocumentSuggestionField.Tags, tagId.ToString());
        await AddSuggestionAsync(id, DocumentSuggestionField.Parties, partyId.ToString());
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
    public async Task ListTags_ParentPathInAnotherCasing_ReturnsItsChildrenWithStoredCasing()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Home/Utilities");
        await CreateTagAsync(admin, "Work/Tax");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var result = await McpToolResults.CallAsync<ListTagsPayload>(client, "list_tags",
            new Dictionary<string, object?> { ["parent_path"] = "home" }, CT);

        Assert.Equal(["Home/Utilities"], result.Items.Select(t => t.Path));
    }

    [Fact]
    public async Task ListTags_SearchWithParentPath_IsAnError()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Home/Utilities");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "list_tags",
            new Dictionary<string, object?> { ["search"] = "util", ["parent_path"] = "Home" }, CT);

        Assert.Equal("Invalid input: Give search or parent_path, not both.", error);
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
            await AddSuggestionAsync(id, DocumentSuggestionField.Name, "Suggested " + name);

        return id;
    }

    private async Task AddSuggestionAsync(
        Guid documentId, DocumentSuggestionField field, string suggestedValue,
        DocumentSuggestionStatus status = DocumentSuggestionStatus.Pending)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = documentId,
            Field = field,
            Status = status,
            SuggestedValue = suggestedValue,
            IsApplied = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(CT);
    }

    private readonly Dictionary<string, Guid> _parties = new(StringComparer.Ordinal);

    private async Task<Guid> CreatePartyAsync(HttpClient admin, string name, string? description = null)
    {
        if (_parties.TryGetValue(name, out var existing))
            return existing;

        var response = await admin.PostAsJsonAsync("/api/v1/documents/parties", new { name, description }, CT);
        response.EnsureSuccessStatusCode();
        var party = await response.Content.ReadFromJsonAsync<CreatedEntity>(CT);
        return _parties[name] = party!.Id;
    }

    private async Task<Guid> CreateTagAsync(HttpClient admin, string path, string? description = null)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/documents/tags", new { path, description }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedEntity>(CT))!.Id;
    }

    private sealed record CreatedEntity(Guid Id);

    // Uploads a PNG as the document's next version over the cookie door and waits for it to be processed.
    private async Task UploadPngVersionAndWaitAsync(HttpClient admin, Guid documentId, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await GetUniqueImageBytesAsync());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);

        var response = await admin.PostAsync($"/api/v1/documents/{documentId}/versions", form, CT);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForJobCompletionAsync((await response.Content.ReadFromJsonAsync<JobResponse>(CT))!.JobId);
    }

    private async Task SetNotesAsync(Guid documentId, string notes)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents.FirstAsync(d => d.Id == documentId, CT);
        document.Notes = notes;
        await db.SaveChangesAsync(CT);
    }

    // A second host over the shared database whose AppDbContext also records every command it runs; both job
    // processors are paused for its lifetime, so no background work lands in the capture.
    private Task<SecondaryHost> StartSqlCapturingHostAsync(SqlCapture sql) =>
        StartSecondaryHostAsync(b => b.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sql))));

    private sealed partial class SqlCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public void Clear() => _commands.Clear();

        // How each captured command touching the extracted-text column uses it — "length" (length() of it),
        // "substr" (a substr()/substring() slice of it), "bare" (the column itself, i.e. the whole text) — one entry per distinct mix.
        public string TextColumnUses() =>
            string.Join(" | ", _commands
                .Where(sql => sql.Contains("\"ExtractedText\"", StringComparison.Ordinal))
                .Select(sql =>
                {
                    var rest = SubstrOfText().Replace(LengthOfText().Replace(sql, ""), "");
                    return string.Join("+", new[]
                    {
                        LengthOfText().IsMatch(sql) ? "length" : null,
                        SubstrOfText().IsMatch(sql) ? "substr" : null,
                        rest.Contains("\"ExtractedText\"", StringComparison.Ordinal) ? "bare" : null,
                    }.OfType<string>());
                })
                .Distinct()
                .Order(StringComparer.Ordinal));

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        [GeneratedRegex(@"length\(\s*(\w+\.)?""ExtractedText""\s*\)")]
        private static partial Regex LengthOfText();

        // Npgsql translates Substring to substring(); substr() is the same function.
        [GeneratedRegex(@"substr(ing)?\(\s*(\w+\.)?""ExtractedText""\s*,")]
        private static partial Regex SubstrOfText();
    }
}
