using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;

public static class MergeBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/merge", async (Guid id,
            MergeBrandRequest body, MergeBrandHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("MergeBrand")
        .Produces<BrandResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
