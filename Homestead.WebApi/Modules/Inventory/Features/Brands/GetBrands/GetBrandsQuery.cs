using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public record GetBrandsQuery : PaginatedRequest, IRequest<PaginatedResponse<BrandResponse>>
{
    /// <summary>Case-insensitive contains search on the brand name. Serves the picker's server-search.</summary>
    public string? Search { get; init; }
}
