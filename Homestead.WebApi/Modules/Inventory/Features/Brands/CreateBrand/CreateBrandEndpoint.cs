using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;

public static class CreateBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateBrandCommand request, ISender sender, CancellationToken ct) =>
        {
            // 201 even for a pre-existing row (find-or-create is idempotent; Tag precedent).
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/inventory/brands/{response.Id}", response);
        })
        .WithName("CreateBrand")
        .Produces<BrandResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
