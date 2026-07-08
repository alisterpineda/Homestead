using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.PatchTag;

public static class PatchTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchInventoryTagModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchInventoryTagCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchInventoryTag")
        .Accepts<JsonPatchDocument<PatchInventoryTagModel>>("application/json-patch+json")
        .Produces<PatchInventoryTagResponse>()
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
