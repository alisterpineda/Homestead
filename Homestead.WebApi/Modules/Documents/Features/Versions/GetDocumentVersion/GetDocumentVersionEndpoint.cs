using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersion;

public static class GetDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid documentId, Guid id,
            GetDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(documentId, id, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocumentVersion")
        .Produces<GetDocumentVersionResponse>()
        .RequireDocumentAuthorization(DocumentOperations.Read);
    }
}
