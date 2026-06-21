using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;

public record GetDurablesQuery : PaginatedRequest, IRequest<PaginatedResponse<DurableResponse>>
{
    /// <summary>
    /// Restrict to items <b>directly placed</b> in this area. This is direct-placement, not resolved —
    /// items nested in a container that resolves to this area are not matched (resolved filtering is
    /// done client-side off <see cref="DurableResponse.ResolvedAreaId"/>).
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
    /// Restrict to items with no <b>direct</b> location (neither area nor container). Like
    /// <see cref="AreaId"/>, this is direct-placement semantics, not resolved-Unplaced.
    /// </summary>
    public bool? Unplaced { get; init; }

    /// <summary>Include terminal (Sold/Discarded/Archived) items. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
