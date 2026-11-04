namespace Homestead.WebApi.Modules.Mcp;

// What the document tools put on the wire. Flat records; McpServiceExtensions serializes them snake_case with nulls
// written (an unset optional field is an explicit null, never a missing key). Dates are yyyy-MM-dd (DateOnly), timestamps UTC ISO 8601 strings, and document ids are the only GUIDs:
// tags travel by path, parties by name, suggestions by their own id. Text derived from the file's content (snippet,
// summary, extracted text) carries the untrusted_ prefix.

// Warnings (every tool with inputs): what was clamped, ignored or contradictory in the call — see McpWarnings.
public sealed record SearchDocumentsResult(
    IReadOnlyList<SearchDocumentsItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<string> DidYouMean,
    IReadOnlyList<string> Warnings);

public sealed record SearchDocumentsItemResult(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string FileExtension,
    string UpdatedAt,
    string? DeletedAt,
    bool PendingReview,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    string? UntrustedSnippet,
    string? UntrustedSummary);

public sealed record GetDocumentResult(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string Notes,
    string? UntrustedSummary,
    string FileExtension,
    long FileSize,
    int VersionCount,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    IReadOnlyList<PendingSuggestionResult> PendingSuggestions,
    string? UntrustedDocumentText,
    int TextTotalChars,
    // Where the latest version's text came from: an engine type slug, or "manual" when a person hand-edited
    // it. Not file-derived, so unprefixed. Null when the version carries no provenance.
    string? TextSource,
    int? NextOffset,
    // The chunk length actually used (after clamping); null without include_text.
    int? TextMaxChars,
    IReadOnlyList<string> Warnings);

// Field is the DocumentSuggestionField name in snake_case (tags, name, parties, document_date, expiry_date), the
// same spelling as update_document's parameters. For Tags/Parties the stored value is an id; SuggestedValue carries
// the tag path / party name instead.
public sealed record PendingSuggestionResult(
    Guid SuggestionId, string Field, string SuggestedValue, string? OriginalValue);

public sealed record ListTagsResult(
    IReadOnlyList<TagItemResult> Items, int Page, int PageSize, int TotalCount, int TotalPages,
    IReadOnlyList<string> Warnings);

public sealed record TagItemResult(string Path, string? Description, int DocumentCount);

public sealed record ListPartiesResult(
    IReadOnlyList<PartyItemResult> Items, int Page, int PageSize, int TotalCount, int TotalPages,
    IReadOnlyList<string> Warnings);

public sealed record PartyItemResult(string Name, string? Description, int DocumentCount);

// --- write tools ---

// ChangedFields: the fields update_document actually changed, named like the result fields (name, document_date,
// expiry_date, notes, tags, parties). Tags are paths, parties names, both sorted.
public sealed record UpdateDocumentResult(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    IReadOnlyList<string> ChangedFields);

public sealed record BulkUpdateDocumentsResult(int UpdatedCount, IReadOnlyList<Guid> NotFoundIds);

public sealed record ResolveSuggestionsResult(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    int ResolvedCount);

public sealed record CreateTagResult(string Path, string? Description, bool Created);

public sealed record CreatePartyResult(string Name, string? Description, bool Created);

public sealed record AddCommentResult(bool Ok);

public sealed record MoveToTrashResult(IReadOnlyList<TrashedDocumentResult> Trashed, IReadOnlyList<Guid> NotFoundIds);

public sealed record TrashedDocumentResult(Guid Id, string Name);
