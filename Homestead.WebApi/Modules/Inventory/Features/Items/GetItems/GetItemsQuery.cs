using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;

public record GetItemsQuery : PaginatedRequest, IRequest<PaginatedResponse<ItemResponse>>
{
    /// <summary>
    /// Restrict to items <b>directly placed</b> in this area. This is direct-placement, not resolved —
    /// items nested in a container that resolves to this area are not matched (resolved filtering is
    /// done client-side off <see cref="ItemResponse.ResolvedAreaId"/>).
    /// </summary>
    public Guid? AreaId { get; init; }

    /// <summary>Restrict to direct children of this container.</summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>Restrict to container / non-container items.</summary>
    public bool? IsContainer { get; init; }

    /// <summary>
    /// Restrict to items with no <b>direct</b> location (neither area nor container). Like
    /// <see cref="AreaId"/>, this is direct-placement semantics, not resolved-Unplaced.
    /// </summary>
    public bool? Unplaced { get; init; }

    /// <summary>Include terminal (Sold/Discarded/Archived) items. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
