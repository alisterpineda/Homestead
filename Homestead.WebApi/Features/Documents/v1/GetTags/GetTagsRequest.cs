using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public record GetTagsRequest : PaginatedRequest
{
    public string? ParentPath { get; init; }
    public bool? IncludeChildren { get; init; }
    public string? Search { get; init; }
    public TagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
