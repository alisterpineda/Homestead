using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.DeleteWarranty;

public static class DeleteItemWarrantyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}/warranties/{warrantyId:guid}", async (Guid id, Guid warrantyId,
            DeleteItemWarrantyHandler handler, CancellationToken ct) =>
        {
            var deleted = await handler.HandleAsync(id, warrantyId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteItemWarranty")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
