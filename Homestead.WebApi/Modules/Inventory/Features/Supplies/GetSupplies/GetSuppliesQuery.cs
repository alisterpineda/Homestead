using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplies;

public record GetSuppliesQuery : PaginatedRequest, IRequest<PaginatedResponse<SupplyResponse>>
{
    /// <summary>Include archived supplies. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Restrict to supplies at or below their restock threshold (the reorder view).</summary>
    public bool? LowStockOnly { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
