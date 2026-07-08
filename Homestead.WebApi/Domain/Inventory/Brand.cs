namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// A canonical brand (consumer manufacturer) an item is attributed to — the axis for grouping
/// "all my Apple devices". Referenced by an optional <see cref="Item.BrandId"/> FK on both durables
/// and consumables. Modelled on <see cref="Area"/> but simpler: no parent, no status, no user
/// attribution — brands are either present or (v1: never) deleted. Canonicalisation rests on
/// entry-time find-or-create plus a unique index on <c>lower(Name)</c>.
/// </summary>
public class Brand
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
