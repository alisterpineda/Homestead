using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class GetDownloadFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", (Guid jobId, GetDownloadFileHandler handler, CancellationToken ct) =>
            handler.HandleAsync(jobId, ct))
        .WithName("GetDownloadFile")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
