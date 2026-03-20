using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public record GetTagsRequest : PaginatedRequest
{
    [FromQuery]
    public string? ParentPath { get; init; }

    [FromQuery]
    public bool IncludeChildren { get; init; }

    [FromQuery]
    public string? Search { get; init; }

    public TagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
