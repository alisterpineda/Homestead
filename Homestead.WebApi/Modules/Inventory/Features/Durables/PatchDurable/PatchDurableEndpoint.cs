using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.PatchDurable;

public static class PatchDurableEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchDurableModel> patchDoc,
            PatchDurableHandler handler, CancellationToken ct) =>
        {
            var command = new PatchDurableCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("PatchDurable")
        .Accepts<JsonPatchDocument<PatchDurableModel>>("application/json-patch+json")
        .Produces<DurableResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
