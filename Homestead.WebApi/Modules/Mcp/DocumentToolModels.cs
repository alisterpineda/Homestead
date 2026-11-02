namespace Homestead.WebApi.Modules.Mcp;

// What the document tools put on the wire. Flat records; McpServiceExtensions serializes them snake_case with nulls
// omitted. Dates are yyyy-MM-dd (DateOnly), timestamps UTC ISO 8601 strings, and document ids are the only GUIDs:
// tags travel by path, parties by name, suggestions by their own id.

public sealed record SearchDocumentsResult(
    IReadOnlyList<SearchDocumentsItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<string> DidYouMean);

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
    string? Snippet,
    string? Summary);

public sealed record GetDocumentResult(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string Notes,
    string? Summary,
    string FileExtension,
    long FileSize,
    int VersionCount,
    string CreatedAt,
    string UpdatedAt,
    IReadOnlyList<DocumentTagResult> Tags,
    IReadOnlyList<DocumentPartyResult> Parties,
    IReadOnlyList<PendingSuggestionResult> PendingSuggestions,
    string? UntrustedDocumentText,
    int TextTotalChars,
    int? NextOffset);

public sealed record DocumentTagResult(string Path, string? Description);

public sealed record DocumentPartyResult(string Name, string? Description);

// Field is the DocumentSuggestionField name (Tags, Name, Parties, DocumentDate, ExpiryDate). For Tags/Parties the
// stored value is an id; SuggestedValue carries the tag path / party name instead.
public sealed record PendingSuggestionResult(
    Guid SuggestionId, string Field, string SuggestedValue, string? OriginalValue);

public sealed record ListTagsResult(
    IReadOnlyList<TagItemResult> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record TagItemResult(string Path, string? Description, int DocumentCount);

public sealed record ListPartiesResult(
    IReadOnlyList<PartyItemResult> Items, int Page, int PageSize, int TotalCount, int TotalPages);

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
