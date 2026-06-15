using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

public static class TrackDurableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/track-one", async (Guid id,
            TrackDurableRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new TrackDurableCommand { ConsumableId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("TrackDurableIndividually")
        .Produces<TrackDurableResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
