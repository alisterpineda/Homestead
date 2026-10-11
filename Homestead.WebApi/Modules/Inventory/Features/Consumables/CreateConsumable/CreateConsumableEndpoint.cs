using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

public static class CreateConsumableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateConsumableCommand request, CreateConsumableHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/inventory/consumables/{response.Id}", response);
        })
        .WithName("CreateConsumable")
        .Produces<ConsumableResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
