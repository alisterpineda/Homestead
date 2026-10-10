using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumableMovements;

/// <summary>Pages a consumable's movement ledger, newest-first. Returns null when the consumable is missing.</summary>
public record GetConsumableMovementsQuery : PaginatedRequest
{
    public Guid Id { get; init; }
}
