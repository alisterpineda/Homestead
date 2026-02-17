namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentFile;

public static class GetDocumentFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/file", async (Guid id, GetDocumentFileHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentFileRequest(id);
            var response = await handler.HandleAsync(request, ct);
            return response is not null
                ? Results.File(response.PhysicalPath, response.ContentType, response.FileName)
                : Results.NotFound();
        })
        .WithName("GetDocumentFile")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
