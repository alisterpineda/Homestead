namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocumentRevision;

public static class DeleteDocumentRevisionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid documentId, Guid id,
            DeleteDocumentRevisionHandler handler, CancellationToken ct) =>
        {
            var request = new DeleteDocumentRevisionRequest(documentId, id);
            return await handler.HandleAsync(request, ct);
        })
        .WithName("DeleteDocumentRevision")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
