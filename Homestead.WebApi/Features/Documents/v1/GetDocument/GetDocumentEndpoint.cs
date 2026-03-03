namespace Homestead.WebApi.Features.Documents.v1.GetDocument;

public static class GetDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocument")
        .Produces<GetDocumentResponse>()
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
