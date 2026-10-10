using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocument;

public static class GetDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, GetDocumentHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentQuery(id);
            var response = await handler.Handle(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocument")
        .Produces<GetDocumentResponse>()
        .RequireDocumentAuthorization(DocumentOperations.Read, routeParameter: "id");
    }
}
