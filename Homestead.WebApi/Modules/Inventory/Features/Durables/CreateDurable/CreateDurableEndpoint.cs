using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;

public static class CreateDurableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateDurableCommand request, CreateDurableHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/inventory/items/{response.Id}", response);
        })
        .WithName("CreateDurable")
        .Produces<DurableResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
