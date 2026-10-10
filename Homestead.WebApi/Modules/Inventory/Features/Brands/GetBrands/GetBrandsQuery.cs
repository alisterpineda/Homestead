using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public record GetBrandsQuery : PaginatedRequest
{
    /// <summary>Case-insensitive contains search on the brand name. Serves the picker's server-search.</summary>
    public string? Search { get; init; }

    /// <summary>Column to sort by: <c>name</c> (default) | <c>items</c> | <c>updated</c>. Serves the management table.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction: <c>asc</c> (default) | <c>desc</c>.</summary>
    public string? SortDir { get; init; }
}
