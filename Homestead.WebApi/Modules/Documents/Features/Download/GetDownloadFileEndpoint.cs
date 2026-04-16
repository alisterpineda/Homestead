using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class GetDownloadFileEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", async (Guid jobId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetDownloadFileQuery(jobId), ct);
            return response;
        })
        .WithName("GetDownloadFile")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
