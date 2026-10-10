using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class GetDownloadFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", async (Guid jobId, GetDownloadFileHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetDownloadFileQuery(jobId), ct);
            return response;
        })
        .WithName("GetDownloadFile")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
