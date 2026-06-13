namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// A named physical place (a room or zone) that anchors where items live. Areas nest into a
/// hierarchy (House › Basement › Bedroom) via an optional self-reference; a null parent is a root.
/// They carry no lifecycle status and no user attribution in v1 — they are either present or deleted.
/// </summary>
public class Area
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Parent area (self-reference). Null for a top-level (root) area. Mirrors <see cref="Item.ParentItemId"/>.</summary>
    public Guid? ParentAreaId { get; set; }
    public Area? ParentArea { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
