using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// A user-authored, back-dateable entry in an item's activity feed — the maintenance/repair log for a
/// durable, a remarks log for a consumable (activity.md §The note). Deliberately just a date and a
/// body, the Documents comment shape plus "when it happened": no subject/kind/cost/performed-by,
/// because any structured field would turn a quick remark into a form.
/// </summary>
public class ItemNote
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    /// <summary>When it happened (UTC, never in the future) — not when it was typed.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The note text (≤4000), trimmed, never empty.</summary>
    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
