using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

/// <summary>
/// Pages an item's activity feed — its audit-log projection interleaved with its notes, newest-first
/// by effective date — for either kind of item. Null only when the item is missing.
/// </summary>
public record GetItemActivityQuery : PaginatedRequest, IRequest<PaginatedResponse<GetItemActivityResponse>?>
{
    public Guid Id { get; init; }

    /// <summary>Drop the audit rows and show notes only.</summary>
    public bool NotesOnly { get; init; }

    /// <summary>Case-insensitive substring over note subject/body; implies <see cref="NotesOnly"/>.</summary>
    public string? Filter { get; init; }
}
