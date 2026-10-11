using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.CreateWarranty;

public static class CreateItemWarrantyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/warranties", async (Guid id, CreateItemWarrantyCommand request,
            CreateItemWarrantyHandler handler, CancellationToken ct) =>
        {
            var command = request with { ItemId = id };
            var response = await handler.HandleAsync(command, ct);
            return Results.Created($"/api/v1/inventory/items/{id}/warranties/{response.Id}", response);
        })
        .WithName("CreateItemWarranty")
        .Produces<ItemWarrantyResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
