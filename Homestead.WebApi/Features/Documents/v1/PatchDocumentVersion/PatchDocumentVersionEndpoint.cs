using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;

public static class PatchDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid documentId, Guid id,
            JsonPatchDocument<PatchDocumentVersionModel> patchDoc,
            PatchDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var command = new PatchDocumentVersionCommand
            {
                DocumentId = documentId,
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await handler.HandleAsync(command, ct));
        })
        .WithName("PatchDocumentVersion")
        .Accepts<JsonPatchDocument<PatchDocumentVersionModel>>("application/json-patch+json")
        .Produces<PatchDocumentVersionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
