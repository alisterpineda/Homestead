using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;

public record GetInventoryTagsQuery : PaginatedRequest, IRequest<PaginatedResponse<GetInventoryTagsResponse>>
{
    public string? ParentPath { get; init; }
    public bool? IncludeChildren { get; init; }
    public string? Search { get; init; }
    public InventoryTagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
