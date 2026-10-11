using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public static class GetBrandsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetBrandsQuery request,
            GetBrandsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetBrands")
        .Produces<PaginatedResponse<BrandResponse>>();
    }
}
