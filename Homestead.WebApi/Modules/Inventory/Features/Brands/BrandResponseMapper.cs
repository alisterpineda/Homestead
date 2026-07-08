using System.Linq.Expressions;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands;

internal static class BrandResponseMapper
{
    /// <summary>EF projection Brand → BrandResponse. Reused by the list query and the create reload.</summary>
    public static readonly Expression<Func<Brand, BrandResponse>> Projection = b => new BrandResponse
    {
        Id = b.Id,
        Name = b.Name,
    };

    public static BrandResponse ToResponse(Brand b) => new()
    {
        Id = b.Id,
        Name = b.Name,
    };
}
