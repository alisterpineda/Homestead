using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
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

    /// <summary>Restrict to direct children of this holder (container or assembly).</summary>
    public Guid? ParentItemId { get; init; }

    /// <summary>
    /// Restrict to items whose holder-role is one of these kinds. Empty/absent ⇒ no kind filter (all
    /// non-fungible items, components included). The placement picker passes
    /// <c>[Container, Assembly]</c>; the list can pass a single kind.
    /// </summary>
    [FromQuery(Name = "DurableKinds")]
    public DurableKind[]? DurableKinds { get; init; }

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
