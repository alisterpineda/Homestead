namespace HomeSuiteHome.WebApi.Features.Documents.v1.DeleteDocument;

public static class DeleteDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new DeleteDocumentRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteDocument")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
