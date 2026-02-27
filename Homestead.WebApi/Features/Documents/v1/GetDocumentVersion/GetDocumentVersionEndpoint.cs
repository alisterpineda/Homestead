namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersion;

public static class GetDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid documentId, Guid id,
            GetDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentVersionRequest(documentId, id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocumentVersion")
        .Produces<GetDocumentVersionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
