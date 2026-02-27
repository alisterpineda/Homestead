namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocumentVersion;

public static class UpdateDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid documentId, Guid id,
            UpdateDocumentVersionRequest request, UpdateDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateDocumentVersionCommand
            {
                DocumentId = documentId,
                Id = id,
                Label = request.Label,
                Notes = request.Notes
            };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("UpdateDocumentVersion")
        .Produces<UpdateDocumentVersionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
