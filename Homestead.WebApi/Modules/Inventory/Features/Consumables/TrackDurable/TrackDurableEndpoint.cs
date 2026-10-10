using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

public static class TrackDurableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/track-one", async (Guid id,
            TrackDurableRequest body, TrackDurableHandler handler, CancellationToken ct) =>
        {
            var command = new TrackDurableCommand { ConsumableId = id, Body = body };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("TrackDurableIndividually")
        .Produces<TrackDurableResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
