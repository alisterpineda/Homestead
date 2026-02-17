namespace HomeSuiteHome.WebApi.Features.Documents.CreateDocument;

public class CreateDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateDocumentRequest request, CreateDocumentHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/documents/{response.DocumentId}", response);
        })
        .WithName("CreateDocument")
        .Produces<CreateDocumentResponse>(StatusCodes.Status201Created);
    }

}