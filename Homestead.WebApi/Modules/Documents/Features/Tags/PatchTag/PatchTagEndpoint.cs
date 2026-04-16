using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.PatchTag;

public static class PatchTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchTagModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchTagCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchTag")
        .Accepts<JsonPatchDocument<PatchTagModel>>("application/json-patch+json")
        .Produces<PatchTagResponse>()
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
