using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.DeleteDocumentVersion;

public static class DeleteDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid documentId, Guid id,
            DeleteDocumentVersionHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(documentId, id, ct);
            return Results.NoContent();
        })
        .WithName("DeleteDocumentVersion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
