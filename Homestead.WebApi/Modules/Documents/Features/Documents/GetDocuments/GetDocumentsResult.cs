namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public record GetDocumentsResult
{
    public required IReadOnlyList<GetDocumentsResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public required IReadOnlyList<string> Suggestions { get; init; }
}
