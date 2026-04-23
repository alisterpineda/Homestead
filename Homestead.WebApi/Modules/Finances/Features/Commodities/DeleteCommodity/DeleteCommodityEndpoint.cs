using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;

public static class DeleteCommodityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var deleted = await sender.Send(new DeleteCommodityCommand(id), ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCommodity")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
