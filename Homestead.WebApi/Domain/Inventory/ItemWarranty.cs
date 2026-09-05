using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// One warranty on a durable (warranties.md §The warranty). Deliberately three optional fields —
/// who backs it, when it lapses, and a one-line qualifier — because anything more (kind enum, start
/// date, duration, claim state) would turn a two-second entry into a form.
/// </summary>
/// <remarks>
/// An item can carry several rows: a manufacturer full warranty plus a limited one on the motor, or
/// a manufacturer warranty plus a retailer extended plan. There is no uniqueness rule and no rollup
/// to an assembly — a component owns its own rows. Durables only; the rule spans tables so it is
/// enforced in the handlers (<c>BusinessRuleException</c>), not by a check constraint.
/// </remarks>
public class ItemWarranty
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    /// <summary>
    /// Who backs the warranty (≤200), or <c>null</c> meaning <b>the manufacturer</b>. The word
    /// "Manufacturer" (with the item's brand name when it has one) is derived at display time and
    /// never copied into this column, so re-branding an item can't strand a stale provider string.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// When the cover lapses, or <c>null</c> meaning <b>no expiry</b> (open-ended / lifetime). A row
    /// with both this and <see cref="Provider"/> null is valid — a lifetime manufacturer warranty.
    /// </summary>
    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Single-line qualifier (≤500), e.g. "motor only"; distinguishes tiers from one another.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
