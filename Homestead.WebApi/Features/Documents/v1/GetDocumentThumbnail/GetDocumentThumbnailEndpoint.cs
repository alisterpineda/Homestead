using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentThumbnail;

public static class GetDocumentThumbnailEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/thumbnail", async (Guid id, Guid? versionId, ISender sender, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=86400";
            var request = new GetDocumentThumbnailQuery(id, versionId);
            return await sender.Send(request, ct);
        })
        .WithName("GetDocumentThumbnail")
        .Produces(StatusCodes.Status200OK, contentType: "image/webp")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
