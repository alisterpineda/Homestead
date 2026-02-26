namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevision;

public static class GetDocumentRevisionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid documentId, Guid id,
            GetDocumentRevisionHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentRevisionRequest(documentId, id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocumentRevision")
        .Produces<GetDocumentRevisionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
