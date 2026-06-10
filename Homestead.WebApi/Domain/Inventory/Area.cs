namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// A flat, named physical place (a room or zone) that anchors where items live. Areas carry no
/// lifecycle status and no user attribution in v1 — they are either present or deleted.
/// </summary>
public class Area
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
