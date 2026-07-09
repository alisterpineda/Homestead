using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands;

internal static class BrandResponseMapper
{
    /// <summary>
    /// In-memory Brand → BrandResponse for the create path (no reload needed). The list/patch/merge read
    /// handlers project inline instead, because <see cref="BrandResponse.ItemCount"/> is a correlated
    /// <c>db.Items.Count(...)</c> subquery a static expression can't reference (Brand has no reverse nav).
    /// </summary>
    public static BrandResponse ToResponse(Brand b, int itemCount) => new()
    {
        Id = b.Id,
        Name = b.Name,
        ItemCount = itemCount,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt,
    };
}
