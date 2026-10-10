namespace Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;

/// <summary>
/// Find-or-create a brand by name. Idempotent: a case-/whitespace-variant of an existing name returns
/// the existing row rather than creating a duplicate (canonicalisation via entry-time dedup + the
/// unique index on <c>lower(Name)</c>). Modelled on Documents' Tag resolve-or-create.
/// </summary>
public record CreateBrandCommand
{
    public required string Name { get; init; }
}
