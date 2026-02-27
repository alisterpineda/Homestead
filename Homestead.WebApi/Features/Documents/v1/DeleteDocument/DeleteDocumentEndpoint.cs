using Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

namespace Homestead.WebApi.Features.Documents.v1.DeleteDocument;

public static class DeleteDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeleteDocumentsHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeleteDocumentsRequest([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
