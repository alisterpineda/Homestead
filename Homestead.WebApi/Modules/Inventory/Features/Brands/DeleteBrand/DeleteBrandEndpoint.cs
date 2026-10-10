using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.DeleteBrand;

public static class DeleteBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteBrandHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.Handle(new DeleteBrandCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteBrand")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
