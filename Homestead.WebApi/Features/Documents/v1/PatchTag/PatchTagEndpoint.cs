using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchTag;

public static class PatchTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchTagModel> patchDoc,
            PatchTagHandler handler, CancellationToken ct) =>
        {
            var command = new PatchTagCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("PatchTag")
        .Accepts<JsonPatchDocument<PatchTagModel>>("application/json-patch+json")
        .Produces<PatchTagResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
