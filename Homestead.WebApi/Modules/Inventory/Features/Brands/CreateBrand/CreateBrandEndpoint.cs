using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;

public static class CreateBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateBrandCommand request, CreateBrandHandler handler, CancellationToken ct) =>
        {
            // 201 even for a pre-existing row (find-or-create is idempotent; Tag precedent).
            var response = await handler.Handle(request, ct);
            return Results.Created($"/api/v1/inventory/brands/{response.Id}", response);
        })
        .WithName("CreateBrand")
        .Produces<BrandResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
