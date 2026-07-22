using Homestead.WebApi.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;

public record GetDurablesQuery : PaginatedRequest, IRequest<PaginatedResponse<DurableResponse>>
{
    /// <summary>
    /// Restrict to items whose <b>resolved</b> area is this area or any of its descendants. Resolved,
    /// not direct-placement: an item nested in a container that walks up to this area (or a sub-area)
    /// is matched. Resolved area is not stored/SQL-expressible, so this filter resolves the full
    /// candidate set in memory before paginating (see <see cref="DurableResponse.ResolvedAreaId"/>).
    /// </summary>
    public Guid? AreaId { get; init; }

    /// <summary>Restrict to direct children of this parent (a container's contents or a regular item's components).</summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>Restrict to items attributed to this brand. Backs the Items-list Brand facet.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>
    /// Restrict to items carrying any of these tags. Backs the Items-list Tag facet. Combined with
    /// <see cref="IncludeChildTags"/> to also match descendant tags.
    /// </summary>
    [FromQuery(Name = "TagIds")]
    public Guid[]? TagIds { get; init; }

    /// <summary>When true, <see cref="TagIds"/> also matches items tagged with any descendant tag.</summary>
    [FromQuery(Name = "IncludeChildTags")]
    public bool? IncludeChildTags { get; init; }

    /// <summary>
    /// Restrict to items whose <b>resolved</b> area is null — the container chain reaches no area (or
    /// the item is directly unplaced). Like <see cref="AreaId"/>, this is resolved, not direct.
    /// </summary>
    public bool? Unplaced { get; init; }

    /// <summary>Include terminal (Sold/Discarded/Archived) items. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
