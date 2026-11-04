using System.ComponentModel;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkDeleteDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkUpdateDocuments;
using Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;
using Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;
using Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;
using Homestead.WebApi.Modules.Documents.Features.Tags.CreateTag;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Homestead.WebApi.Modules.Mcp;

// The write tools over the Documents module. Every method carries [Authorize(RequireContributor)] (hidden from and
// rejected for read-only callers by AddAuthorizationFilters) and runs its work in McpToolInvoker.Write*Async — the
// role gate again, one transaction, one exception mapping. Tools taking one document id use WriteDocumentAsync
// (authorizes Write by that id first); the bulk handlers authorize every id themselves, all-or-nothing, so the bulk
// tools use WriteAsync and never catch a denial to retry per id. Tags and parties to add must already exist
// (TagPathSnapper / PartyNameResolver); only create_tag / create_party create them.
// Instantiated per call by the SDK from the request's services — never register it in DI (ignored).
[McpServerToolType]
public sealed class DocumentWriteTools(
    McpToolInvoker invoker,
    TagPathSnapper tagSnapper,
    PartyNameResolver partyResolver,
    McpDocumentReads documentReads,
    PatchDocumentHandler patchDocument,
    BulkUpdateDocumentsHandler bulkUpdateDocuments,
    BulkDeleteDocumentsHandler bulkDeleteDocuments,
    ApproveSuggestionHandler approveSuggestion,
    DeclineSuggestionHandler declineSuggestion,
    ApproveAllSuggestionsHandler approveAllSuggestions,
    DeclineAllSuggestionsHandler declineAllSuggestions,
    CreateTagHandler createTag,
    CreatePartyHandler createParty,
    CreateCommentHandler createComment,
    IDocumentQueryService documentQuery,
    AppDbContext db)
{
    [McpServerTool(Name = "update_document", Title = "Update document",
         ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Edit one document. Tags and parties must already exist (see list_tags / list_parties). `notes` " +
                 "REPLACES all notes — prefer `append_note`. Names follow the house style in the server instructions. " +
                 "Pending AI suggestions for a field you change are auto-declined, as in the web app.")]
    public Task<UpdateDocumentResult> UpdateDocument(
        [Description("A document id from search_documents.")] Guid document_id,
        [Description("New name.")] string? name = null,
        [Description("New document date (yyyy-MM-dd); \"\" clears it.")] string? document_date = null,
        [Description("New expiry date (yyyy-MM-dd); \"\" clears it.")] string? expiry_date = null,
        [Description("Replaces ALL notes; \"\" clears them. Not combinable with append_note.")] string? notes = null,
        [Description("A paragraph to add after the existing notes.")] string? append_note = null,
        [Description("Tag paths from list_tags to add.")] string[]? add_tags = null,
        [Description("Tag paths to remove (case-insensitive; a path not on the document is ignored).")] string[]? remove_tags = null,
        [Description("Party names from list_parties to link (case-insensitive).")] string[]? add_parties = null,
        [Description("Party names to unlink (case-insensitive; a name not linked is ignored).")] string[]? remove_parties = null,
        CancellationToken ct = default) =>
        invoker.WriteDocumentAsync(document_id, async c =>
        {
            // The editable fields only; the response below comes from the patch handler's own reload.
            var current = await documentReads.EditStateAsync(document_id, c)
                          ?? throw new EntityNotFoundException($"document {document_id} (it may be in the trash).");

            var addTagPaths = add_tags is { Length: > 0 }
                ? (await tagSnapper.RequireKnownAsync(add_tags, c)).Select(t => t.Path).ToList()
                : [];
            var addParties = add_parties is { Length: > 0 }
                ? await partyResolver.RequireKnownAsync(add_parties, c)
                : [];

            var edit = UpdateDocumentPatchBuilder.Build(
                current.State,
                new UpdateDocumentInput
                {
                    Name = name,
                    DocumentDate = document_date,
                    ExpiryDate = expiry_date,
                    Notes = notes,
                    AppendNote = append_note,
                    AddTagPaths = addTagPaths,
                    RemoveTagPaths = remove_tags ?? [],
                    AddParties = addParties,
                    RemovePartyNames = remove_parties ?? [],
                });

            var updated = await patchDocument.HandleAsync(document_id, edit.Patch, c);

            return new UpdateDocumentResult(
                updated.Id,
                updated.Name,
                updated.DocumentDate,
                updated.ExpiryDate,
                updated.Tags.Select(t => t.Path).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                updated.Parties.Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                edit.ChangedFields);
        }, ct);

    [McpServerTool(Name = "bulk_update_documents", Title = "Bulk update documents",
         ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Add or remove tags and parties on many documents at once; other tags and parties are untouched. " +
                 "Fails as a whole if any document is not writable.")]
    public Task<BulkUpdateDocumentsResult> BulkUpdateDocuments(
        [Description("1 to 100 document ids from search_documents.")] Guid[] document_ids,
        [Description("Tag paths from list_tags to add.")] string[]? add_tags = null,
        [Description("Tag paths to remove (case-insensitive).")] string[]? remove_tags = null,
        [Description("Party names from list_parties to link (case-insensitive).")] string[]? add_parties = null,
        [Description("Party names to unlink (case-insensitive).")] string[]? remove_parties = null,
        CancellationToken ct = default) =>
        invoker.WriteAsync(async c =>
        {
            // The checks BulkUpdateDocumentsEndpoint makes before its handler, plus the cap.
            var ids = McpIdSets.RequireIds(document_ids, McpIdSets.BulkUpdateMaxIds, nameof(document_ids)).ToList();

            var addTagInputs = NormalizeTagPaths(add_tags);
            var removeTagPaths = NormalizeTagPaths(remove_tags);
            var addPartyNames = TrimPartyNames(add_parties);
            var removePartyNames = TrimPartyNames(remove_parties);

            if (addTagInputs.Count + removeTagPaths.Count + addPartyNames.Count + removePartyNames.Count == 0)
                throw new ArgumentException("Give at least one of add_tags, remove_tags, add_parties or remove_parties.");
            if (addTagInputs.Intersect(removeTagPaths, StringComparer.OrdinalIgnoreCase).Any())
                throw new ArgumentException("Tags cannot be both added and removed in the same request.");
            if (addPartyNames.Intersect(removePartyNames, StringComparer.OrdinalIgnoreCase).Any())
                throw new ArgumentException("Parties cannot be both added and removed in the same request.");

            var addTagPaths = addTagInputs.Count > 0
                ? (await tagSnapper.RequireKnownAsync(addTagInputs, c)).Select(t => t.Path).ToList()
                : null;
            var addPartyIds = addPartyNames.Count > 0
                ? (await partyResolver.RequireKnownAsync(addPartyNames, c)).Select(p => p.Id).ToList()
                : null;
            // Every casing of a removed name (removal is case-insensitive; an unknown name links nothing).
            var removePartyIds = removePartyNames.Count > 0 ? await PartyIdsByNameAsync(removePartyNames, c) : null;

            var notFound = await NotFoundIdsAsync(ids, c);

            var response = await bulkUpdateDocuments.HandleAsync(new BulkUpdateDocumentsCommand(
                ids,
                addTagPaths,
                addPartyIds,
                removeTagPaths.Count > 0 ? removeTagPaths : null,
                removePartyIds is { Count: > 0 } ? removePartyIds : null), c);

            return new BulkUpdateDocumentsResult(response.UpdatedCount, notFound);
        }, ct);

    [McpServerTool(Name = "resolve_suggestions", Title = "Resolve AI suggestions",
         ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Approve keeps the AI's suggested value; decline reverts to the original. Act only on suggestions " +
                 "the user asked you to review.")]
    public Task<ResolveSuggestionsResult> ResolveSuggestions(
        [Description("A document id from search_documents.")] Guid document_id,
        [Description("suggestion_id values from get_document to approve.")] Guid[]? approve_ids = null,
        [Description("suggestion_id values from get_document to decline.")] Guid[]? decline_ids = null,
        [Description("\"approve\" or \"decline\" every pending suggestion on the document. Not combinable with approve_ids / decline_ids.")] string? all = null,
        CancellationToken ct = default) =>
        invoker.WriteDocumentAsync(document_id, async c =>
        {
            var approve = (approve_ids ?? []).Distinct().ToList();
            var decline = (decline_ids ?? []).Distinct().ToList();
            var mode = ParseAll(all);

            if (mode is not null && approve.Count + decline.Count > 0)
                throw new ArgumentException("Give all, or approve_ids / decline_ids, not both.");
            if (mode is null && approve.Count + decline.Count == 0)
                throw new ArgumentException("Give approve_ids, decline_ids or all.");
            if (approve.Intersect(decline).Any())
                throw new ArgumentException("A suggestion cannot be both approved and declined.");

            // The pending-suggestion count is what "all" resolves.
            var before = await documentReads.EditStateAsync(document_id, c)
                         ?? throw new EntityNotFoundException($"document {document_id} (it may be in the trash).");

            int resolved;
            switch (mode)
            {
                case SuggestionResolution.Approve:
                    await approveAllSuggestions.HandleAsync(document_id, c);
                    resolved = before.PendingSuggestionCount;
                    break;
                case SuggestionResolution.Decline:
                    await declineAllSuggestions.HandleAsync(document_id, c);
                    resolved = before.PendingSuggestionCount;
                    break;
                default:
                    // An unknown or already-resolved id throws and rolls the whole call back.
                    foreach (var id in approve)
                        await approveSuggestion.HandleAsync(document_id, id, c);
                    foreach (var id in decline)
                        await declineSuggestion.HandleAsync(document_id, id, c);
                    resolved = approve.Count + decline.Count;
                    break;
            }

            // Declining reverts values; read the row back inside the transaction.
            var after = await documentReads.EditStateAsync(document_id, c)
                        ?? throw new EntityNotFoundException($"document {document_id} (it may be in the trash).");

            return new ResolveSuggestionsResult(
                after.Id,
                after.State.Name,
                after.State.DocumentDate,
                after.State.ExpiryDate,
                after.State.TagPaths.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                after.State.Parties.Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                resolved);
        }, ct);

    [McpServerTool(Name = "create_tag", Title = "Create tag",
         ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Create a tag only when the user agrees to a new one; parent tags are created automatically. An " +
                 "existing path is returned unchanged.")]
    public Task<CreateTagResult> CreateTag(
        [Description("The new tag's /-delimited path, e.g. Home/Utilities/Water.")] string path,
        [Description("What the tag is for.")] string? description = null,
        CancellationToken ct = default) =>
        invoker.WriteAsync(async c =>
        {
            // CreateTagHandler is find-or-create and would overwrite an existing tag's description, so an existing
            // path (any casing) is answered here without calling it.
            var candidates = TagPathResolver.NormalizeAndBuildCandidatePaths(path).CandidatePaths;
            var leaf = candidates[^1];
            var known = (await tagSnapper.SnapAsync(candidates, c)).Known;

            if (known.FirstOrDefault(t => string.Equals(t.Path, leaf, StringComparison.OrdinalIgnoreCase)) is { } existing)
                return new CreateTagResult(existing.Path, existing.Description, Created: false);

            // Keep the deepest existing ancestor's stored casing, so "home/Garden" under "Home" never forks a second
            // "home" branch (Tag.Path's unique index is case-sensitive).
            var ancestor = known
                .Where(t => leaf.StartsWith(t.Path + "/", StringComparison.OrdinalIgnoreCase))
                .MaxBy(t => t.Path.Length);
            var target = ancestor is null ? leaf : ancestor.Path + leaf[ancestor.Path.Length..];

            var created = await createTag.HandleAsync(
                new CreateTagCommand(target, null, string.IsNullOrWhiteSpace(description) ? null : description.Trim()), c);

            return new CreateTagResult(created.Path, created.Description, Created: true);
        }, ct);

    [McpServerTool(Name = "create_party", Title = "Create party",
         ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Create a party only for a real person or organisation the user named. An existing name is returned " +
                 "unchanged.")]
    public Task<CreatePartyResult> CreateParty(
        [Description("The person's or organisation's name.")] string name,
        [Description("Who they are, e.g. electricity provider.")] string? description = null,
        CancellationToken ct = default) =>
        invoker.WriteAsync(async c =>
        {
            // The same trim every other party-name input gets, so what is stored is what later lookups match.
            var trimmed = PartyNameResolver.NormalizeName(name ?? "");

            // Case-insensitive, like every other name lookup here (Party.Name's unique index is case-sensitive, so
            // CreatePartyHandler's own duplicate check would let "ACME" fork "Acme").
            if ((await partyResolver.ResolveAsync([trimmed], c)).Known is [var existing, ..])
            {
                var existingDescription = await db.Parties.AsNoTracking()
                    .Where(p => p.Id == existing.Id)
                    .Select(p => p.Description)
                    .FirstOrDefaultAsync(c);
                return new CreatePartyResult(existing.Name, existingDescription, Created: false);
            }

            var created = await createParty.HandleAsync(new CreatePartyCommand
            {
                Name = trimmed,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
            }, c);

            return new CreatePartyResult(created.Name, created.Description, Created: true);
        }, ct);

    [McpServerTool(Name = "add_comment", Title = "Add comment",
         ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Leave a permanent comment in the document's activity stream, e.g. why a change was made. Start " +
                 "with 'Via Claude:'. Use only when the user asks you to record something; never in loops. Comments " +
                 "cannot be edited.")]
    public Task<AddCommentResult> AddComment(
        [Description("A document id from search_documents.")] Guid document_id,
        [Description("The comment text.")] string body,
        CancellationToken ct = default) =>
        invoker.WriteDocumentAsync(document_id, async c =>
        {
            await createComment.HandleAsync(new CreateCommentCommand(body ?? "") { DocumentId = document_id }, c);
            return new AddCommentResult(Ok: true);
        }, ct);

    [McpServerTool(Name = "move_to_trash", Title = "Move to trash",
         ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
     Authorize(Policy = AuthPolicies.RequireContributor),
     Description("Soft-delete documents the user explicitly confirmed, by id from search results, never by name. " +
                 "Restorable from Trash in the web app only.")]
    public Task<MoveToTrashResult> MoveToTrash(
        [Description("1 to 20 document ids from search_documents.")] Guid[] document_ids,
        CancellationToken ct = default) =>
        invoker.WriteAsync(async c =>
        {
            var ids = McpIdSets.RequireIds(document_ids, McpIdSets.TrashMaxIds, nameof(document_ids)).ToList();

            // Names come from the access-filtered, active-only source: what the caller can see is what gets trashed
            // (the handler throws, all-or-nothing, on any id it finds but the caller may not write).
            var found = await documentQuery.AccessibleDocuments()
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(c);
            var notFound = McpIdSets.Missing(ids, found.Select(d => d.Id));

            await bulkDeleteDocuments.HandleAsync(new BulkDeleteDocumentsCommand(ids), c);

            var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            return new MoveToTrashResult(
                found.OrderBy(d => order[d.Id]).Select(d => new TrashedDocumentResult(d.Id, d.Name)).ToList(),
                notFound);
        }, ct);

    private async Task<IReadOnlyList<Guid>> NotFoundIdsAsync(List<Guid> ids, CancellationToken ct)
    {
        var found = await documentQuery.AccessibleDocuments()
            .Where(d => ids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);
        return McpIdSets.Missing(ids, found);
    }

    private async Task<List<Guid>> PartyIdsByNameAsync(List<string> names, CancellationToken ct)
    {
        return await db.Parties.AsNoTracking()
            .WhereNameIn(p => p.Name, names)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    // Normalised as any tag write would (an empty path is an ArgumentException), distinct case-insensitively.
    private static List<string> NormalizeTagPaths(string[]? paths) =>
        (paths ?? [])
        .Select(TagPathResolver.NormalizePath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<string> TrimPartyNames(string[]? names) =>
        (names ?? [])
        .Select(PartyNameResolver.NormalizeName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private enum SuggestionResolution { Approve, Decline }

    private static SuggestionResolution? ParseAll(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "approve" => SuggestionResolution.Approve,
        "decline" => SuggestionResolution.Decline,
        _ => throw new ArgumentException($"all must be approve or decline, got '{value}'."),
    };
}
