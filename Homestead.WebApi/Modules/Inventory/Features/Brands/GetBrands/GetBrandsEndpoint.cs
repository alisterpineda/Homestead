using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.GetBrands;

public static class GetBrandsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetBrandsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetBrands")
        .Produces<PaginatedResponse<BrandResponse>>();
    }
}
