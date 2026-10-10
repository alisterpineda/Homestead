using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

/// <summary>
/// Pages an item's activity feed — its audit-log projection interleaved with its notes, newest-first
/// by effective date — for either kind of item. Null only when the item is missing.
/// </summary>
public record GetItemActivityQuery : PaginatedRequest
{
    public Guid Id { get; init; }
}
