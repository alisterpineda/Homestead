using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentFile;

public static class GetDocumentFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/file", async (Guid id, Guid? versionId, bool? download, ISender sender, CancellationToken ct) =>
        {
            var request = new GetDocumentFileRequest(id, versionId, download);
            return await sender.Send(request, ct);
        })
        .WithName("GetDocumentFile")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
