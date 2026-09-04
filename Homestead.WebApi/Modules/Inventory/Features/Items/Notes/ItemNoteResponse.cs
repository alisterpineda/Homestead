namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes;

/// <summary>
/// A saved note as the create/update endpoints return it. Carries the creator/updater display name
/// and email so the client can swap the row into the feed without a reload.
/// </summary>
public record ItemNoteResponse
{
    public required Guid Id { get; init; }
    public required Guid ItemId { get; init; }

    /// <summary>When it happened — the feed's effective date for this row.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Body { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>True once any field has been changed after creation (<c>UpdatedAt != CreatedAt</c>).</summary>
    public required bool IsEdited { get; init; }

    public string? CreatedByDisplayName { get; init; }
    public string? CreatedByEmail { get; init; }
    public string? UpdatedByDisplayName { get; init; }
    public string? UpdatedByEmail { get; init; }
}
