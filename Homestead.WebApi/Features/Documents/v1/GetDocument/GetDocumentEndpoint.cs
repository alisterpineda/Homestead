namespace Homestead.WebApi.Features.Documents.v1.GetDocument;

public static class GetDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentRequest(id);
            return await handler.HandleAsync(request, ct);
        })
        .WithName("GetDocument")
        .Produces<GetDocumentResponse>()
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
