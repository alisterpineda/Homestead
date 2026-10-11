using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;

public static class DeleteCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteCommodityHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.HandleAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCommodity")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
