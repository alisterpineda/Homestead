using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;

public static class CollapseDurablesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/collapse", async (Guid id,
            CollapseDurablesRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CollapseDurablesCommand { ConsumableId = id, Body = body };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("CollapseDurables")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
