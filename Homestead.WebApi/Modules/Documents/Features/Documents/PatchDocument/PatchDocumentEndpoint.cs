using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.PatchDocument;

public static class PatchDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/{id:guid}", async (Guid id,
            JsonPatchDocument<PatchDocumentModel> patchDoc,
            PatchDocumentHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, patchDoc, ct)))
        .WithName("PatchDocument")
        .Accepts<JsonPatchDocument<PatchDocumentModel>>("application/json-patch+json")
        .Produces<PatchDocumentResponse>()
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write, routeParameter: "id");
    }
}
