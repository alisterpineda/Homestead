using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.GetSupplyMovements;

/// <summary>Pages a supply's movement ledger, newest-first. Returns null when the supply is missing.</summary>
public record GetSupplyMovementsQuery : PaginatedRequest, IRequest<PaginatedResponse<StockMovementResponse>?>
{
    public Guid Id { get; init; }
}
