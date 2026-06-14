using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

/// <summary>
/// Pages a non-fungible item's metadata activity history (its audit log), newest-first. Returns
/// null when the item is missing — supplies, which keep a movement ledger instead, also 404 here.
/// </summary>
public record GetItemActivityQuery : PaginatedRequest, IRequest<PaginatedResponse<GetItemActivityResponse>?>
{
    public Guid Id { get; init; }
}
