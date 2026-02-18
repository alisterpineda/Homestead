namespace HomeSuiteHome.WebApi.Features.Documents.v1.UpdateDocument;

public static class UpdateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateDocumentRequest request,
            UpdateDocumentHandler handler, CancellationToken ct) =>
        {
            var command = request with { Id = id };
            var response = await handler.HandleAsync(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateDocument")
        .Produces<UpdateDocumentResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
