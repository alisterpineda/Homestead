using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTags;

public record GetTagsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetTagsResponse>>
{
    public string? ParentPath { get; init; }
    public bool? IncludeChildren { get; init; }
    public string? Search { get; init; }
    public TagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
