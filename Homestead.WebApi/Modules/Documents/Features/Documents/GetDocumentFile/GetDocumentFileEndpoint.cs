using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentFile;

public static class GetDocumentFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/file", (Guid id, Guid? versionId, bool? download, GetDocumentFileHandler handler, CancellationToken ct) =>
            handler.HandleAsync(id, versionId, download, ct))
        .WithName("GetDocumentFile")
        .Produces(StatusCodes.Status200OK)
        .RequireDocumentAuthorization(DocumentOperations.Read, routeParameter: "id");
    }
}
