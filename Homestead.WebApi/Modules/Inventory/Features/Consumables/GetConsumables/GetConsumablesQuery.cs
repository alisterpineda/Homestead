using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;

public record GetConsumablesQuery : PaginatedRequest, IRequest<PaginatedResponse<ConsumableResponse>>
{
    /// <summary>Include archived consumables. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Restrict to consumables at or below their restock threshold (the reorder view).</summary>
    public bool? LowStockOnly { get; init; }

    /// <summary>
    /// Restrict to consumables with stock whose <b>resolved</b> area is this area or any descendant —
    /// a consumable matches when <b>any</b> of its stock locations walks up to the area (or a
    /// sub-area). Resolved area is not stored/SQL-expressible, so this resolves the full candidate set
    /// in memory before paginating. A consumable with no stock locations matches neither this nor
    /// <see cref="Unplaced"/>.
    /// </summary>
    public Guid? AreaId { get; init; }

    /// <summary>
    /// Restrict to consumables with stock whose <b>resolved</b> area is null — any stock location's
    /// container chain reaches no area (a broken/unplaced chain). Like <see cref="AreaId"/>, resolved.
    /// </summary>
    public bool? Unplaced { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
