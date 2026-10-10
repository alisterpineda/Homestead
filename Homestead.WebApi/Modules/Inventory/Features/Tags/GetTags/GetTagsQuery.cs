using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;

public record GetInventoryTagsQuery : PaginatedRequest
{
    public string? ParentPath { get; init; }
    public bool? IncludeChildren { get; init; }
    public string? Search { get; init; }
    public InventoryTagSortBy? SortBy { get; init; }
    public SortDirection? SortDir { get; init; }
}
