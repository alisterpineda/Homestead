using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public static class DeleteDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid documentId, Guid id,
            DeleteDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new DeleteDocumentVersionRequest(documentId, id);
            await handler.HandleAsync(request, ct);
            return Results.NoContent();
        })
        .WithName("DeleteDocumentVersion")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
