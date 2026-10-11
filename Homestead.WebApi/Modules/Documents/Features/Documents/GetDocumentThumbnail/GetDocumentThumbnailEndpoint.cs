using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.GetDocumentThumbnail;

public static class GetDocumentThumbnailEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/thumbnail", async (Guid id, Guid? versionId, GetDocumentThumbnailHandler handler, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=86400";
            return await handler.HandleAsync(id, versionId, ct);
        })
        .WithName("GetDocumentThumbnail")
        .Produces(StatusCodes.Status200OK, contentType: "image/webp")
        .RequireDocumentAuthorization(DocumentOperations.Read, routeParameter: "id");
    }
}
