namespace Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;

/// <summary>Request body for merge: the brand to keep.</summary>
public record MergeBrandRequest(Guid TargetId);
