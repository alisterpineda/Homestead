namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public record GetDocumentsResult
{
    public required IReadOnlyList<GetDocumentsResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public required IReadOnlyList<string> Suggestions { get; init; }

    /// <summary>The same "Did you mean…?" names with where each comes from (<c>document</c>, <c>tag</c> or <c>party</c>).</summary>
    public required IReadOnlyList<SearchSuggestion> SuggestionDetails { get; init; }
}

public sealed record SearchSuggestion(string Kind, string Value);
