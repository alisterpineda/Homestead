using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.UpdateWarranty;

public static class UpdateItemWarrantyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}/warranties/{warrantyId:guid}", async (Guid id, Guid warrantyId,
            UpdateItemWarrantyCommand request, ISender sender, CancellationToken ct) =>
        {
            var command = request with { ItemId = id, WarrantyId = warrantyId };
            var response = await sender.Send(command, ct);
            return Results.Ok(response);
        })
        .WithName("UpdateItemWarranty")
        .Produces<ItemWarrantyResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
