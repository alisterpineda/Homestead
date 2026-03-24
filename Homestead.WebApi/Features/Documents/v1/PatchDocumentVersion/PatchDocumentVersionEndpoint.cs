using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Documents.v1.PatchDocumentVersion;

public static class PatchDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid documentId, Guid id,
            JsonPatchDocument<PatchDocumentVersionModel> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchDocumentVersionCommand
            {
                DocumentId = documentId,
                Id = id,
                PatchDocument = patchDoc
            };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchDocumentVersion")
        .Accepts<JsonPatchDocument<PatchDocumentVersionModel>>("application/json-patch+json")
        .Produces<PatchDocumentVersionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
