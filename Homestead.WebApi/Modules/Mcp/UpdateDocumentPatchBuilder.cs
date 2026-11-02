using System.Text.Json;
using Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;
using ModelContextProtocol;

namespace Homestead.WebApi.Modules.Mcp;

// update_document's one PatchDocumentHandler call, composed from the document's current state and the requested
// changes — the UI's single-document editor shape: tags and parties travel as the full post-delta set. A field gets a
// replace op only when its value actually changes, because the handler auto-declines a field's pending suggestions on
// ANY op touching its path, even a no-op. Pure: the tool snaps added tag paths and resolves added party names first.
public static class UpdateDocumentPatchBuilder
{
    public const string NothingToUpdateMessage = "Nothing to update.";

    // Ops in PatchDocumentModel order; ChangedFields are the ops' fields in snake_case (tag_paths, party_ids, …).
    public static UpdateDocumentPatch Build(DocumentEditState current, UpdateDocumentInput input)
    {
        var ops = new List<(string Path, string Field, object? Value)>();

        if (input.Name is { } requestedName && requestedName.Trim() is var name && name != current.Name)
            ops.Add(("/name", "name", name));

        if (ParseClearableDate(input.DocumentDate, "document_date") is (true, var documentDate)
            && documentDate != current.DocumentDate)
            ops.Add(("/documentDate", "document_date", documentDate));

        if (ParseClearableDate(input.ExpiryDate, "expiry_date") is (true, var expiryDate)
            && expiryDate != current.ExpiryDate)
            ops.Add(("/expiryDate", "expiry_date", expiryDate));

        if (ComposeNotes(current.Notes, input) is { } notes && notes != current.Notes)
            ops.Add(("/notes", "notes", notes));

        // changed_fields names the result fields (tags / parties), not PatchDocumentModel's paths.
        if (TagPathsAfterDelta(current.TagPaths, input) is { } tagPaths)
            ops.Add(("/tagPaths", "tags", tagPaths));

        if (PartyIdsAfterDelta(current.Parties, input) is { } partyIds)
            ops.Add(("/partyIds", "parties", partyIds));

        if (ops.Count == 0)
            throw new McpException(NothingToUpdateMessage);

        // Web options (camelCase, case-insensitive), as when the endpoint deserializes a patch: ApplyTo resolves the
        // camelCase paths against the model through them.
        var patch = new JsonPatchDocument<PatchDocumentModel> { SerializerOptions = JsonSerializerOptions.Web };
        foreach (var (path, _, value) in ops)
            patch.Operations.Add(new Operation<PatchDocumentModel>("replace", path, from: null, value));

        return new UpdateDocumentPatch(patch, ops.Select(o => o.Field).ToList());
    }

    // null = not given; "" (or blank) = clear; anything else strict yyyy-MM-dd.
    private static (bool Given, DateOnly? Value) ParseClearableDate(string? value, string parameterName) =>
        value is null ? (false, null)
        : string.IsNullOrWhiteSpace(value) ? (true, null)
        : (true, McpDates.ParseOptional(value, parameterName));

    // notes is a full replace; append_note adds a paragraph (no leading separator on empty notes). A blank
    // append_note is treated as not given.
    private static string? ComposeNotes(string currentNotes, UpdateDocumentInput input)
    {
        var append = string.IsNullOrWhiteSpace(input.AppendNote) ? null : input.AppendNote.Trim();
        if (input.Notes is not null && append is not null)
            throw new ArgumentException("Give notes or append_note, not both.");

        if (input.Notes is not null)
            return input.Notes;
        if (append is null)
            return null;

        return string.IsNullOrWhiteSpace(currentNotes) ? append : currentNotes.TrimEnd() + "\n\n" + append;
    }

    // Removals are normalised like any tag path and matched case-insensitively (a path not on the document is a
    // no-op); additions are stored paths already. Null when the set does not change.
    private static List<string>? TagPathsAfterDelta(IReadOnlyList<string> current, UpdateDocumentInput input)
    {
        var remove = input.RemoveTagPaths
            .Select(TagPathResolver.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (input.AddTagPaths.FirstOrDefault(remove.Contains) is { } both)
            throw new ArgumentException($"Tag '{both}' cannot be both added and removed.");

        var result = current.Where(p => !remove.Contains(p)).ToList();
        foreach (var path in input.AddTagPaths)
        {
            if (!result.Contains(path, StringComparer.Ordinal))
                result.Add(path);
        }

        return SameSet(current, result, StringComparer.Ordinal) ? null : result;
    }

    // Removals match the document's linked parties by name, trimmed and case-insensitive (a name not linked is a
    // no-op); additions are resolved parties. Null when the set does not change.
    private static List<Guid>? PartyIdsAfterDelta(IReadOnlyList<ResolvedParty> current, UpdateDocumentInput input)
    {
        var remove = input.RemovePartyNames
            .Select(PartyNameResolver.NormalizeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (input.AddParties.FirstOrDefault(p => remove.Contains(p.Name)) is { } both)
            throw new ArgumentException($"Party '{both.Name}' cannot be both added and removed.");

        var currentIds = current.Select(p => p.Id).ToList();
        var result = current.Where(p => !remove.Contains(p.Name)).Select(p => p.Id).ToList();
        foreach (var party in input.AddParties)
        {
            if (!result.Contains(party.Id))
                result.Add(party.Id);
        }

        return SameSet(currentIds, result, EqualityComparer<Guid>.Default) ? null : result;
    }

    private static bool SameSet<T>(IEnumerable<T> a, IEnumerable<T> b, IEqualityComparer<T> comparer) =>
        a.ToHashSet(comparer).SetEquals(b);
}

// The fields update_document may change, as the document has them now.
public sealed record DocumentEditState(
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string Notes,
    IReadOnlyList<string> TagPaths,
    IReadOnlyList<ResolvedParty> Parties);

// The requested changes. Null scalars = not given; dates are raw strings ("" clears). AddTagPaths are snapped (stored
// casing) and AddParties resolved by the tool; removals are raw paths / names.
public sealed record UpdateDocumentInput
{
    public string? Name { get; init; }
    public string? DocumentDate { get; init; }
    public string? ExpiryDate { get; init; }
    public string? Notes { get; init; }
    public string? AppendNote { get; init; }
    public IReadOnlyList<string> AddTagPaths { get; init; } = [];
    public IReadOnlyList<string> RemoveTagPaths { get; init; } = [];
    public IReadOnlyList<ResolvedParty> AddParties { get; init; } = [];
    public IReadOnlyList<string> RemovePartyNames { get; init; } = [];
}

public sealed record UpdateDocumentPatch(JsonPatchDocument<PatchDocumentModel> Patch, IReadOnlyList<string> ChangedFields);
