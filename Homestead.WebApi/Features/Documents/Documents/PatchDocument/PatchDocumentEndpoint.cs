using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.Documents.PatchDocument;

public static class PatchDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchDocumentModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchDocumentCommand
            {
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchDocument")
        .Accepts<JsonPatchDocument<PatchDocumentModel>>("application/json-patch+json")
        .Produces<PatchDocumentResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
