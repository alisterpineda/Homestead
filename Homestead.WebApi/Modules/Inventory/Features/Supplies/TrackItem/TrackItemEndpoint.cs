using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.TrackItem;

public static class TrackItemEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/track-one", async (Guid id,
            TrackItemRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new TrackItemCommand { SupplyId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("TrackItemIndividually")
        .Produces<TrackItemResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
