namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentFile;

public static class GetDocumentFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/file", async (Guid id, bool? download, GetDocumentFileHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentFileRequest(id);
            var response = await handler.HandleAsync(request, ct);
            if (response is null)
                return Results.NotFound();

            return download == true
                ? Results.File(response.PhysicalPath, response.ContentType, response.Name)
                : Results.File(response.PhysicalPath, response.ContentType);
        })
        .WithName("GetDocumentFile")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
