namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public static class UpdateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateDocumentRequest request,
            UpdateDocumentHandler handler, CancellationToken ct) =>
        {
            var command = new UpdateDocumentCommand
            {
                Id = id,
                Name = request.Name,
                DocumentDate = request.DocumentDate,
                ValidFrom = request.ValidFrom,
                ValidUntil = request.ValidUntil,
                TagPaths = request.TagPaths,
                PartyIds = request.PartyIds
            };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("UpdateDocument")
        .Produces<UpdateDocumentResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
