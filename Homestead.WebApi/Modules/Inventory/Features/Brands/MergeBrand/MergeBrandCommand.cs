namespace Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;

/// <summary>Request body for merge: the brand to keep.</summary>
public record MergeBrandRequest(Guid TargetId);

/// <summary>
/// Consolidate a source brand into a target: re-point every <c>Item.BrandId</c> from source to target,
/// then delete the source. This is the one job entry-time find-or-create can't do — collapse true
/// duplicates ("Apple" / "Apple Inc") that differ by more than casing.
/// </summary>
public record MergeBrandCommand
{
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
}
