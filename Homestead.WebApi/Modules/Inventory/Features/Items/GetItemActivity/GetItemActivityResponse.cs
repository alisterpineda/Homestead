namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

/// <summary>
/// One row of an item's activity feed. A flat union discriminated by <see cref="Type"/>:
/// <c>"audit_log"</c> rows carry <see cref="Summary"/>; <c>"note"</c> rows carry the note fields
/// (<see cref="Body"/> through <see cref="UpdatedByEmail"/>).
/// </summary>
public record GetItemActivityResponse
{
    /// <summary><c>"audit_log"</c> or <c>"note"</c>.</summary>
    public required string Type { get; init; }

    public required Guid Id { get; init; }

    /// <summary>
    /// The row's effective date — the note's <see cref="OccurredAt"/> (which may be back-dated) or the
    /// audit row's recorded time. This is what the feed sorts on (activity.md §Sorting).
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The actor: the audit row's user, or the note's creator.</summary>
    public string? UserDisplayName { get; init; }
    public string? UserEmail { get; init; }

    // ----- audit_log only -----
    public string? Summary { get; init; }

    // ----- note only -----
    public string? Body { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>True for a note changed after creation; always false for audit rows.</summary>
    public bool IsEdited { get; init; }

    public string? UpdatedByDisplayName { get; init; }
    public string? UpdatedByEmail { get; init; }
}
