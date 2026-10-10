using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;

public static class CollapseDurablesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/collapse", async (Guid id,
            CollapseDurablesRequest body, CollapseDurablesHandler handler, CancellationToken ct) =>
        {
            var command = new CollapseDurablesCommand { ConsumableId = id, Body = body };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("CollapseDurables")
        .Produces<ConsumableDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
