using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;

public static class PatchConsumableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchConsumableModel> patchDoc,
            PatchConsumableHandler handler, CancellationToken ct) =>
        {
            var command = new PatchConsumableCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("PatchConsumable")
        .Accepts<JsonPatchDocument<PatchConsumableModel>>("application/json-patch+json")
        .Produces<ConsumableResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
