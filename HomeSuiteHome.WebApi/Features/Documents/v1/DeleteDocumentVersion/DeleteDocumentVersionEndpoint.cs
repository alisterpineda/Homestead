namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocumentVersion;

public static class DeleteDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid documentId, Guid id,
            DeleteDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new DeleteDocumentVersionRequest(documentId, id);
            return await handler.HandleAsync(request, ct);
        })
        .WithName("DeleteDocumentVersion")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
