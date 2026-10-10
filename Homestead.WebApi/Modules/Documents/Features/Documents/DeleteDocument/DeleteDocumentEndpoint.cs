using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Documents.Features.Documents.BulkDeleteDocuments;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.DeleteDocument;

public static class DeleteDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeleteDocumentsCommand([id]);
            var response = await handler.Handle(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
