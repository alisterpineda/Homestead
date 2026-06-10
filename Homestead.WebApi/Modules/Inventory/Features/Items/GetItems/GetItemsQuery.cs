using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;

public record GetItemsQuery : PaginatedRequest, IRequest<PaginatedResponse<ItemResponse>>
{
    /// <summary>Restrict to items directly placed in this area.</summary>
    public Guid? AreaId { get; init; }

    /// <summary>Restrict to items with no location (neither area nor container).</summary>
    public bool? Unplaced { get; init; }

    /// <summary>Include terminal (Sold/Discarded/Archived) items. Default false.</summary>
    public bool? IncludeInactive { get; init; }

    /// <summary>Case-insensitive name search.</summary>
    public string? Search { get; init; }
}
