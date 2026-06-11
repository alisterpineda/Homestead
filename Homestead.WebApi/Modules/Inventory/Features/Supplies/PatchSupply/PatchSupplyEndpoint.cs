using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.PatchSupply;

public static class PatchSupplyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchSupplyModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchSupplyCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchSupply")
        .Accepts<JsonPatchDocument<PatchSupplyModel>>("application/json-patch+json")
        .Produces<SupplyResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
