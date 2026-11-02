using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// The write tools end to end through a real MCP client: what each one changes (read back from the database or the
/// cookie door), who the change is attributed to, and that a denial or a mid-call failure changes nothing. Documents
/// are seeded by an Admin over the cookie door; the MCP user is a separate bearer account.
/// </summary>
[Collection("Integration")]
public class DocumentWriteToolsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly string[] WriteTools =
    [
        "update_document", "bulk_update_documents", "resolve_suggestions", "create_tag", "create_party", "add_comment",
        "move_to_trash",
    ];

    // --- update_document ---

    [Fact]
    public async Task UpdateDocument_AddsAndRemovesTagsAndParties_AndSetsNameAndDate()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedEditableDocumentAsync(admin);
        await using var client = await ConnectAsync(AppRole.Contributor);

        await CallUpdateAsync(client, id);

        Assert.Equal(
            new DocumentState("Hydro bill 2024-03", new DateOnly(2024, 3, 15), "Home/Keep, Home/New", "New Co"),
            await DocumentStateAsync(id));
    }

    [Fact]
    public async Task UpdateDocument_RecordsOneActivityEntry_AttributedToTheMcpUser()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedEditableDocumentAsync(admin);
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);
        var before = await AuditEntriesAsync(admin, id);

        await CallUpdateAsync(client, id);

        var added = (await AuditEntriesAsync(admin, id)).Where(a => before.All(b => b.Id != a.Id)).ToList();
        Assert.Equal([user.Email], added.Select(a => a.UserEmail));
    }

    [Fact]
    public async Task UpdateDocument_NameChange_DeclinesThePendingNameSuggestion_LeavesTheTagsSuggestionPending()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        var tagId = await CreateTagAsync(admin, "Home/Insurance");
        var nameSuggestion = await AddSuggestionAsync(id, DocumentSuggestionField.Name, "AI name", "scan.pdf");
        var tagSuggestion = await AddSuggestionAsync(id, DocumentSuggestionField.Tags, tagId.ToString());
        await using var client = await ConnectAsync(AppRole.Contributor);

        await McpToolResults.CallAsync<UpdateDocumentPayload>(client, "update_document",
            new Dictionary<string, object?> { ["document_id"] = id, ["name"] = "Home insurance policy" }, CT);

        Assert.Equal(
            (Name: DocumentSuggestionStatus.Declined, Tags: DocumentSuggestionStatus.Pending),
            (Name: await SuggestionStatusAsync(nameSuggestion), Tags: await SuggestionStatusAsync(tagSuggestion)));
    }

    // The plan's variant: name validation runs before PatchDocumentHandler's only flush, so nothing was written
    // when it fails — this proves no partial change, not a rollback (ResolveSuggestions_* below does that).
    [Fact]
    public async Task UpdateDocument_BlankNameWithATag_FailsWithoutLinkingTheTag()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        await CreateTagAsync(admin, "Home/Insurance");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var error = await McpToolResults.CallForErrorAsync(client, "update_document", new Dictionary<string, object?>
        {
            ["document_id"] = id, ["add_tags"] = new[] { "Home/Insurance" }, ["name"] = "  ",
        }, CT);

        Assert.Equal(
            ("Name is required.", new DocumentState("scan.pdf", null, "", "")),
            (error, await DocumentStateAsync(id)));
    }

    [Fact]
    public async Task UpdateDocument_ContributorWithReadOnlyGroupAccess_IsNotFoundOrNoAccess_AndChangesNothing()
    {
        var admin = await CreateAdminClientAsync();
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        var group = await CreateGroupAsync(admin, "MCP Read-only Group");
        await AddGroupMemberAsync(admin, group, user.Id, accessLevel: "Viewer");
        var id = await UploadAndWaitAsync(admin, "group.pdf", group);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var error = await McpToolResults.CallForErrorAsync(client, "update_document",
            new Dictionary<string, object?> { ["document_id"] = id, ["name"] = "Renamed" }, CT);

        Assert.Equal(
            (true, "group.pdf"),
            (error.StartsWith("Not found or no access", StringComparison.Ordinal), (await DocumentStateAsync(id)).Name));
    }

    // --- read-only roles ---

    [Fact]
    public async Task Viewer_DoesNotSeeTheWriteTools()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var tools = await client.ListToolsAsync(cancellationToken: CT);

        Assert.Empty(tools.Select(t => t.Name).Intersect(WriteTools));
    }

    [Fact]
    public async Task Viewer_CallingAWriteToolDirectly_IsRejected_AndChangesNothing()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        await using var client = await ConnectAsync(AppRole.Viewer);

        var rejected = await IsRejectedAsync(client, "update_document",
            new Dictionary<string, object?> { ["document_id"] = id, ["name"] = "Renamed" });

        Assert.Equal((true, "scan.pdf"), (rejected, (await DocumentStateAsync(id)).Name));
    }

    // --- bulk_update_documents ---

    [Fact]
    public async Task BulkUpdate_WithADocumentTheCallerCannotWrite_FailsAsAWhole_AndChangesNothing()
    {
        var admin = await CreateAdminClientAsync();
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        var group = await CreateGroupAsync(admin, "MCP Read-only Group");
        await AddGroupMemberAsync(admin, group, user.Id, accessLevel: "Viewer");
        var writable = await UploadAndWaitAsync(admin, "household.pdf");
        var readOnly = await UploadAndWaitAsync(admin, "group.pdf", group);
        await CreateTagAsync(admin, "Home/Utilities");
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var error = await McpToolResults.CallForErrorAsync(client, "bulk_update_documents", new Dictionary<string, object?>
        {
            ["document_ids"] = new[] { writable, readOnly }, ["add_tags"] = new[] { "Home/Utilities" },
        }, CT);

        Assert.Equal(
            (true, ""),
            (error.StartsWith("Not found or no access", StringComparison.Ordinal), (await DocumentStateAsync(writable)).Tags));
    }

    [Fact]
    public async Task BulkUpdate_UnknownIds_AreReportedAsNotFound_TheRestAreUpdated()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        await CreateTagAsync(admin, "Home/Utilities");
        var unknown = Guid.NewGuid();
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<BulkUpdatePayload>(client, "bulk_update_documents",
            new Dictionary<string, object?>
            {
                ["document_ids"] = new[] { id, unknown }, ["add_tags"] = new[] { "home/utilities" },
            }, CT);

        Assert.Equal(
            (1, unknown.ToString(), "Home/Utilities"),
            (result.UpdatedCount, Csv(result.NotFoundIds), (await DocumentStateAsync(id)).Tags));
    }

    [Fact]
    public async Task BulkUpdate_RemoveTags_UnlinksTheTagCaseInsensitively_LeavesTheRest()
    {
        var admin = await CreateAdminClientAsync();
        var id = await SeedEditableDocumentAsync(admin);
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<BulkUpdatePayload>(client, "bulk_update_documents",
            new Dictionary<string, object?> { ["document_ids"] = new[] { id }, ["remove_tags"] = new[] { "home/old" } },
            CT);

        Assert.Equal((1, "Home/Keep"), (result.UpdatedCount, (await DocumentStateAsync(id)).Tags));
    }

    // The removal lookup matches every casing of the stored name.
    [Fact]
    public async Task BulkUpdate_RemovePartiesInAnotherCasing_UnlinksTheStoredParty()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        var acme = await CreatePartyAsync(admin, "Acme Power");
        var other = await CreatePartyAsync(admin, "Other Co");
        (await SendJsonPatchAsync(admin, $"/api/v1/documents/{id}", new[]
        {
            new { op = "replace", path = "/partyIds", value = (object)new[] { acme, other } },
        })).EnsureSuccessStatusCode();
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<BulkUpdatePayload>(client, "bulk_update_documents",
            new Dictionary<string, object?> { ["document_ids"] = new[] { id }, ["remove_parties"] = new[] { "acme power" } },
            CT);

        Assert.Equal((1, "Other Co"), (result.UpdatedCount, (await DocumentStateAsync(id)).Parties));
    }

    [Fact]
    public async Task BulkUpdate_SameTagAddedAndRemoved_IsAnError_AndChangesNothing()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        await CreateTagAsync(admin, "Home/Utilities");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var error = await McpToolResults.CallForErrorAsync(client, "bulk_update_documents", new Dictionary<string, object?>
        {
            ["document_ids"] = new[] { id }, ["add_tags"] = new[] { "Home/Utilities" },
            ["remove_tags"] = new[] { "home/utilities" },
        }, CT);

        Assert.Equal(
            ("Invalid input: Tags cannot be both added and removed in the same request.", ""),
            (error, (await DocumentStateAsync(id)).Tags));
    }

    // --- create_tag / create_party ---

    // The creation path: the deepest existing ancestor keeps its stored casing, so a lower-cased path never forks a
    // second branch (Tag.Path's unique index is case-sensitive). Two existing levels, so the deepest — not the
    // shallowest — ancestor must be the one kept.
    [Fact]
    public async Task CreateTag_NewPathUnderAnExistingBranchInAnotherCasing_KeepsTheStoredCasing()
    {
        var admin = await CreateAdminClientAsync();
        await CreateTagAsync(admin, "Home/Garden");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<CreateTagPayload>(client, "create_tag",
            new Dictionary<string, object?> { ["path"] = "home/garden/Beds", ["description"] = "Raised beds" }, CT);

        using var scope = CreateScope();
        var tags = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tags.AsNoTracking()
            .Select(t => new { t.Path, t.Description }).ToListAsync(CT);
        Assert.Equal(
            (Created: true, Path: "Home/Garden/Beds", Description: "Raised beds",
             Stored: "Home, Home/Garden, Home/Garden/Beds", StoredDescription: "Raised beds"),
            (result.Created, result.Path, result.Description,
             Stored: Csv(tags.Select(t => t.Path).Order(StringComparer.Ordinal)),
             StoredDescription: tags.SingleOrDefault(t => t.Path == "Home/Garden/Beds")?.Description));
    }

    [Fact]
    public async Task CreateParty_NewName_CreatesIt()
    {
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<CreatePartyPayload>(client, "create_party",
            new Dictionary<string, object?> { ["name"] = "  Acme Power  ", ["description"] = "Electricity provider" }, CT);

        using var scope = CreateScope();
        var parties = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Parties.AsNoTracking()
            .Select(p => new { p.Name, p.Description }).ToListAsync(CT);
        var stored = Assert.Single(parties);
        Assert.Equal(
            (true, "Acme Power", "Electricity provider", "Acme Power", "Electricity provider"),
            (result.Created, result.Name, result.Description, stored.Name, stored.Description));
    }

    [Fact]
    public async Task CreateTag_ExistingPath_ReturnsItUnchanged_WithoutTouchingColourOrDescription()
    {
        var admin = await CreateAdminClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/documents/tags",
            new { path = "Home/Utilities", color = "#ef4444", description = "Bills for the house" }, CT))
            .EnsureSuccessStatusCode();
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<CreateTagPayload>(client, "create_tag",
            new Dictionary<string, object?> { ["path"] = "home/utilities", ["description"] = "Something else" }, CT);

        using var scope = CreateScope();
        var tag = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tags.AsNoTracking()
            .SingleAsync(t => t.Path == "Home/Utilities", CT);
        Assert.Equal(
            (Created: false, Path: "Home/Utilities", Color: "#ef4444", Description: "Bills for the house"),
            (result.Created, result.Path, tag.Color, tag.Description));
    }

    [Fact]
    public async Task CreateParty_ExistingName_ReturnsItUnchanged_AndCreatesNoSecondParty()
    {
        var admin = await CreateAdminClientAsync();
        await CreatePartyAsync(admin, "Acme Power");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<CreatePartyPayload>(client, "create_party",
            new Dictionary<string, object?> { ["name"] = "acme power" }, CT);

        using var scope = CreateScope();
        var parties = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Parties.AsNoTracking()
            .Select(p => p.Name).ToListAsync(CT);
        Assert.Equal((false, "Acme Power", "Acme Power"), (result.Created, result.Name, Csv(parties)));
    }

    // --- resolve_suggestions ---

    [Fact]
    public async Task ResolveSuggestions_Approve_KeepsTheSuggestedValue()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        await using var client = await ConnectAsync(AppRole.Contributor);

        await ResolveAsync(client, id, new() { ["approve_ids"] = new[] { nameSuggestion } });

        Assert.Equal(
            ("AI name", DocumentSuggestionStatus.Approved),
            ((await DocumentStateAsync(id)).Name, await SuggestionStatusAsync(nameSuggestion)));
    }

    [Fact]
    public async Task ResolveSuggestions_Decline_RestoresTheOriginalValue()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        await using var client = await ConnectAsync(AppRole.Contributor);

        await ResolveAsync(client, id, new() { ["decline_ids"] = new[] { nameSuggestion } });

        Assert.Equal(
            ("original.pdf", DocumentSuggestionStatus.Declined),
            ((await DocumentStateAsync(id)).Name, await SuggestionStatusAsync(nameSuggestion)));
    }

    [Fact]
    public async Task ResolveSuggestions_AllApprove_KeepsEverySuggestedValue()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        var tagSuggestion = await AddAppliedTagSuggestionAsync(admin, id, "Home/Insurance");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await ResolveAsync(client, id, new() { ["all"] = "approve" });

        Assert.Equal(
            (2, new DocumentState("AI name", null, "Home/Insurance", ""), "Approved, Approved"),
            (result.ResolvedCount, await DocumentStateAsync(id),
             $"{await SuggestionStatusAsync(nameSuggestion)}, {await SuggestionStatusAsync(tagSuggestion)}"));
    }

    [Fact]
    public async Task ResolveSuggestions_AllDecline_RevertsEverySuggestedValue()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        var tagSuggestion = await AddAppliedTagSuggestionAsync(admin, id, "Home/Insurance");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await ResolveAsync(client, id, new() { ["all"] = "decline" });

        // The result reports the reverted state too (read back after the declines), not the state before them.
        Assert.Equal(
            (2, new DocumentState("original.pdf", null, "", ""), "Declined, Declined",
             Result: new DocumentState("original.pdf", null, "", ""), ResultExpiry: (DateOnly?)null),
            (result.ResolvedCount, await DocumentStateAsync(id),
             $"{await SuggestionStatusAsync(nameSuggestion)}, {await SuggestionStatusAsync(tagSuggestion)}",
             Result: new DocumentState(result.Name, result.DocumentDate, Csv(result.Tags), Csv(result.Parties)),
             ResultExpiry: result.ExpiryDate));
    }

    // "all" resolves what is pending; an already-resolved suggestion is neither counted nor touched.
    [Fact]
    public async Task ResolveSuggestions_All_ResolvesAndCountsOnlyThePendingSuggestions()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        var declined = await AddSuggestionAsync(
            id, DocumentSuggestionField.DocumentDate, "2024-01-01", status: DocumentSuggestionStatus.Declined);
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await ResolveAsync(client, id, new() { ["all"] = "approve" });

        Assert.Equal(
            (1, "Approved, Declined"),
            (result.ResolvedCount,
             $"{await SuggestionStatusAsync(nameSuggestion)}, {await SuggestionStatusAsync(declined)}"));
    }

    // The rollback test. The first decline reverts the name and flushes it (DeclineSuggestionHandler's
    // SaveChangesAsync opens the tool's transaction and writes the document, its audit row and the suggestion); the
    // unknown id then throws inside the same WriteDocumentAsync frame, so the whole call must roll back.
    [Fact]
    public async Task ResolveSuggestions_FailingAfterAFlushedDecline_RollsTheWholeCallBack()
    {
        var admin = await CreateAdminClientAsync();
        var (id, nameSuggestion) = await SeedAppliedNameSuggestionAsync(admin);
        var auditBefore = (await AuditEntriesAsync(admin, id)).Count;
        await using var client = await ConnectAsync(AppRole.Contributor);

        var error = await McpToolResults.CallForErrorAsync(client, "resolve_suggestions", new Dictionary<string, object?>
        {
            ["document_id"] = id, ["decline_ids"] = new[] { nameSuggestion, Guid.NewGuid() },
        }, CT);

        Assert.Equal(
            ("Not found: Suggestion not found.", "AI name", DocumentSuggestionStatus.Pending, auditBefore),
            (error, (await DocumentStateAsync(id)).Name, await SuggestionStatusAsync(nameSuggestion),
             (await AuditEntriesAsync(admin, id)).Count));
    }

    // --- add_comment ---

    [Fact]
    public async Task AddComment_AppearsInTheActivityStream_AttributedToTheMcpUser()
    {
        var admin = await CreateAdminClientAsync();
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        await McpToolResults.CallAsync<AddCommentPayload>(client, "add_comment",
            new Dictionary<string, object?> { ["document_id"] = id, ["body"] = "Via Claude: tagged as a utility bill." },
            CT);

        var comments = (await ActivityAsync(admin, id)).Where(a => a.Type == "comment")
            .Select(a => (a.Body, a.UserEmail));
        Assert.Equal([("Via Claude: tagged as a utility bill.", user.Email)], comments);
    }

    // --- move_to_trash ---

    [Fact]
    public async Task MoveToTrash_SoftDeletes_TheDocumentIsInTheTrash_AndRestorable()
    {
        var admin = await CreateAdminClientAsync();
        var trashed = await UploadAndWaitAsync(admin, "old-bill.pdf");
        await UploadAndWaitAsync(admin, "kept.pdf");
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<MoveToTrashPayload>(client, "move_to_trash",
            new Dictionary<string, object?> { ["document_ids"] = new[] { trashed } }, CT);
        var active = await SearchNamesAsync(client, inTrash: false);
        var inTrash = await SearchNamesAsync(client, inTrash: true);
        var restore = await admin.PostAsync($"/api/v1/documents/{trashed}/restore", null, CT);
        var afterRestore = await SearchNamesAsync(client, inTrash: false);

        Assert.Equal(
            (Trashed: "old-bill.pdf", Active: "kept.pdf", InTrash: "old-bill.pdf", Restore: HttpStatusCode.NoContent,
             AfterRestore: "kept.pdf, old-bill.pdf"),
            (Trashed: Csv(result.Trashed.Select(t => t.Name)), Active: active, InTrash: inTrash, Restore: restore.StatusCode,
             AfterRestore: afterRestore));
    }

    [Fact]
    public async Task MoveToTrash_UnknownId_IsReportedAsNotFound_TheRestAreTrashed()
    {
        var admin = await CreateAdminClientAsync();
        var trashed = await UploadAndWaitAsync(admin, "old-bill.pdf");
        var unknown = Guid.NewGuid();
        await using var client = await ConnectAsync(AppRole.Contributor);

        var result = await McpToolResults.CallAsync<MoveToTrashPayload>(client, "move_to_trash",
            new Dictionary<string, object?> { ["document_ids"] = new[] { trashed, unknown } }, CT);

        Assert.Equal(
            (Trashed: trashed.ToString(), NotFound: unknown.ToString(), Deleted: true),
            (Trashed: Csv(result.Trashed.Select(t => t.Id)), NotFound: Csv(result.NotFoundIds),
             Deleted: await IsTrashedAsync(trashed)));
    }

    // All-or-nothing: one listed document the caller can see but not write fails the call, and nothing is trashed.
    [Fact]
    public async Task MoveToTrash_WithADocumentTheCallerCannotWrite_FailsAsAWhole_AndTrashesNothing()
    {
        var admin = await CreateAdminClientAsync();
        var (user, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        var group = await CreateGroupAsync(admin, "MCP Read-only Group");
        await AddGroupMemberAsync(admin, group, user.Id, accessLevel: "Viewer");
        var writable = await UploadAndWaitAsync(admin, "household.pdf");
        var readOnly = await UploadAndWaitAsync(admin, "group.pdf", group);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var error = await McpToolResults.CallForErrorAsync(client, "move_to_trash",
            new Dictionary<string, object?> { ["document_ids"] = new[] { writable, readOnly } }, CT);

        Assert.Equal(
            (true, false, false),
            (error.StartsWith("Not found or no access", StringComparison.Ordinal),
             await IsTrashedAsync(writable), await IsTrashedAsync(readOnly)));
    }

    // --- helpers ---

    private async Task<McpClient> ConnectAsync(AppRole role)
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, role);
        return await McpClientHelper.ConnectAsync(Factory, token, CT);
    }

    // A hidden tool may be refused as a JSON-RPC error (thrown) or as an IsError result; either way it is refused.
    private static async Task<bool> IsRejectedAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        try
        {
            var result = await client.CallToolAsync(tool, arguments, cancellationToken: CT);
            return result.IsError == true;
        }
        catch (McpException)
        {
            return true;
        }
    }

    // Tagged Home/Old + Home/Keep and linked to Old Co; Home/New and New Co exist but are not on it.
    private async Task<Guid> SeedEditableDocumentAsync(HttpClient admin)
    {
        var id = await UploadAndWaitAsync(admin, "scan.pdf");
        await CreateTagAsync(admin, "Home/New");
        await CreatePartyAsync(admin, "New Co");
        var oldCo = await CreatePartyAsync(admin, "Old Co");
        (await SendJsonPatchAsync(admin, $"/api/v1/documents/{id}", new object[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { "Home/Old", "Home/Keep" } },
            new { op = "replace", path = "/partyIds", value = (object)new[] { oldCo } },
        })).EnsureSuccessStatusCode();
        return id;
    }

    // Snapped casing on the way in (home/new → Home/New), case-insensitive removals.
    private static Task<UpdateDocumentPayload> CallUpdateAsync(McpClient client, Guid id) =>
        McpToolResults.CallAsync<UpdateDocumentPayload>(client, "update_document", new Dictionary<string, object?>
        {
            ["document_id"] = id,
            ["name"] = "Hydro bill 2024-03",
            ["document_date"] = "2024-03-15",
            ["add_tags"] = new[] { "home/new" },
            ["remove_tags"] = new[] { "home/old" },
            ["add_parties"] = new[] { "New Co" },
            ["remove_parties"] = new[] { "old co" },
        }, CT);

    // A document the AI renamed: Name = "AI name", with a pending applied Name suggestion whose original was the
    // uploaded name.
    private async Task<(Guid DocumentId, Guid SuggestionId)> SeedAppliedNameSuggestionAsync(HttpClient admin)
    {
        var id = await UploadAndWaitAsync(admin, "original.pdf");
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await db.Documents.FirstAsync(d => d.Id == id, CT);
            document.Name = "AI name";
            await db.SaveChangesAsync(CT);
        }
        return (id, await AddSuggestionAsync(id, DocumentSuggestionField.Name, "AI name", "original.pdf"));
    }

    // Links the tag the way the UI does, then records it as an applied, pending AI suggestion.
    private async Task<Guid> AddAppliedTagSuggestionAsync(HttpClient admin, Guid id, string path)
    {
        var tagId = await CreateTagAsync(admin, path);
        (await SendJsonPatchAsync(admin, $"/api/v1/documents/{id}", new[]
        {
            new { op = "replace", path = "/tagPaths", value = (object)new[] { path } },
        })).EnsureSuccessStatusCode();
        return await AddSuggestionAsync(id, DocumentSuggestionField.Tags, tagId.ToString());
    }

    private async Task<Guid> AddSuggestionAsync(
        Guid documentId, DocumentSuggestionField field, string suggestedValue, string? originalValue = null,
        DocumentSuggestionStatus status = DocumentSuggestionStatus.Pending)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = new DocumentSuggestion
        {
            DocumentId = documentId,
            Field = field,
            Status = status,
            SuggestedValue = suggestedValue,
            OriginalValue = originalValue,
            IsApplied = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.DocumentSuggestions.Add(suggestion);
        await db.SaveChangesAsync(CT);
        return suggestion.Id;
    }

    private async Task<DocumentSuggestionStatus> SuggestionStatusAsync(Guid suggestionId)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DocumentSuggestions.AsNoTracking()
            .Where(s => s.Id == suggestionId).Select(s => s.Status).SingleAsync(CT);
    }

    private static Task<ResolveSuggestionsPayload> ResolveAsync(
        McpClient client, Guid id, Dictionary<string, object?> arguments)
    {
        arguments["document_id"] = id;
        return McpToolResults.CallAsync<ResolveSuggestionsPayload>(client, "resolve_suggestions", arguments, CT);
    }

    // Tags and parties as ordered CSV, so the record compares by value.
    private sealed record DocumentState(string Name, DateOnly? DocumentDate, string Tags, string Parties);

    private async Task<DocumentState> DocumentStateAsync(Guid id)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var document = await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
            .Include(d => d.DocumentParties).ThenInclude(dp => dp.Party)
            .SingleAsync(d => d.Id == id, CT);
        return new DocumentState(
            document.Name,
            document.DocumentDate,
            Csv(document.DocumentTags.Select(dt => dt.Tag.Path).Order(StringComparer.Ordinal)),
            Csv(document.DocumentParties.Select(dp => dp.Party.Name).Order(StringComparer.Ordinal)));
    }

    private async Task<bool> IsTrashedAsync(Guid id)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Documents.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.Id == id).Select(d => d.DeletedAt != null).SingleAsync(CT);
    }

    private static async Task<List<ActivityItem>> ActivityAsync(HttpClient admin, Guid id)
    {
        var page = await admin.GetFromJsonAsync<PaginatedResult<ActivityItem>>(
            $"/api/v1/documents/{id}/activity?Page=1&PageSize=100", CT);
        return page!.Items.ToList();
    }

    private static async Task<List<ActivityItem>> AuditEntriesAsync(HttpClient admin, Guid id) =>
        (await ActivityAsync(admin, id)).Where(a => a.Type == "audit_log").ToList();

    private static async Task<string> SearchNamesAsync(McpClient client, bool inTrash)
    {
        var result = await McpToolResults.CallAsync<SearchDocumentsPayload>(client, "search_documents",
            new Dictionary<string, object?> { ["in_trash"] = inTrash }, CT);
        return Csv(result.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
    }

    private static string Csv<T>(IEnumerable<T> values) => string.Join(", ", values);

    private static async Task<Guid> CreatePartyAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/documents/parties", new { name }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedEntity>(CT))!.Id;
    }

    private static async Task<Guid> CreateTagAsync(HttpClient admin, string path)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/documents/tags", new { path }, CT);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedEntity>(CT))!.Id;
    }

    private sealed record CreatedEntity(Guid Id);

    private sealed record ActivityItem(string Type, Guid Id, string? UserEmail, string? Summary, string? Body);
}
