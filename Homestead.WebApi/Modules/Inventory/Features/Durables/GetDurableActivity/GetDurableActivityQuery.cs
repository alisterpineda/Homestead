using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurableActivity;

/// <summary>
/// Pages a non-fungible item's metadata activity history (its audit log), newest-first. Returns
/// null when the item is missing — consumables, which keep a movement ledger instead, also 404 here.
/// </summary>
public record GetDurableActivityQuery : PaginatedRequest, IRequest<PaginatedResponse<GetDurableActivityResponse>?>
{
    public Guid Id { get; init; }
}
