namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocuments;

public static class GetDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetDocumentsQuery request,
            GetDocumentsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetDocuments")
        .Produces<GetDocumentsResult>();
    }
}
