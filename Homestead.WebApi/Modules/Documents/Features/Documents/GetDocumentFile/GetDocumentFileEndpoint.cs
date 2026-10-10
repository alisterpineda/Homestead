using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentFile;

public static class GetDocumentFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/file", async (Guid id, Guid? versionId, bool? download, GetDocumentFileHandler handler, CancellationToken ct) =>
        {
            var request = new GetDocumentFileQuery(id, versionId, download);
            return await handler.Handle(request, ct);
        })
        .WithName("GetDocumentFile")
        .Produces(StatusCodes.Status200OK)
        .RequireDocumentAuthorization(DocumentOperations.Read, routeParameter: "id");
    }
}
