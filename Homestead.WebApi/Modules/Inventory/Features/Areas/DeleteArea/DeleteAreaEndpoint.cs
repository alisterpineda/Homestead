using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;

public static class DeleteAreaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteAreaHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.Handle(new DeleteAreaCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteArea")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
