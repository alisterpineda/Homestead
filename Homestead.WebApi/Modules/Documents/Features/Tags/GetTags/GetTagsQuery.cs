using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTags;

public record GetTagsQuery : PaginatedRequest
{
    public string? ParentPath { get; init; }
    public bool? IncludeChildren { get; init; }
    public string? Search { get; init; }
    public TagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
