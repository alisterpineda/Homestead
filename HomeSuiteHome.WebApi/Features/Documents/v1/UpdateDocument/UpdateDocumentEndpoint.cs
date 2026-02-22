namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public static class UpdateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateDocumentRequest request,
            UpdateDocumentHandler handler, CancellationToken ct) =>
        {
            var command = request with { Id = id };
            return await handler.HandleAsync(command, ct);
        })
        .WithName("UpdateDocument")
        .Produces<UpdateDocumentResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
