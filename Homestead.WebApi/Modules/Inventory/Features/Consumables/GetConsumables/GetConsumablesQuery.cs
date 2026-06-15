using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;

public record GetConsumablesQuery : PaginatedRequest, IRequest<PaginatedResponse<ConsumableResponse>>
{
    /// <summary>Include archived consumables. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Restrict to consumables at or below their restock threshold (the reorder view).</summary>
    public bool? LowStockOnly { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
