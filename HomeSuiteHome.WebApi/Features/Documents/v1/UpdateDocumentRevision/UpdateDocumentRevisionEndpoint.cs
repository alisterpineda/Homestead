namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocumentRevision;

public static class UpdateDocumentRevisionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid documentId, Guid id,
            UpdateDocumentRevisionRequest request, UpdateDocumentRevisionHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateDocumentRevisionCommand
            {
                DocumentId = documentId,
                Id = id,
                Label = request.Label,
                Notes = request.Notes
            };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("UpdateDocumentRevision")
        .Produces<UpdateDocumentRevisionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
