using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;

public static class PatchDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid documentId, Guid id,
            JsonPatchDocument<PatchDocumentVersionModel> patchDoc,
            PatchDocumentVersionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(documentId, id, patchDoc, ct)))
        .WithName("PatchDocumentVersion")
        .Accepts<JsonPatchDocument<PatchDocumentVersionModel>>("application/json-patch+json")
        .Produces<PatchDocumentVersionResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
