using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;

public static class PatchAreaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchAreaModel> patchDoc,
            PatchAreaHandler handler, CancellationToken ct) =>
        {
            var command = new PatchAreaCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.Handle(command, ct));
        })
        .WithName("PatchArea")
        .Accepts<JsonPatchDocument<PatchAreaModel>>("application/json-patch+json")
        .Produces<AreaResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
