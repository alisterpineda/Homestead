using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Export;

public static class DownloadExportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}/download", async (Guid jobId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new DownloadExportQuery(jobId), ct);
            return response;
        })
        .WithName("DownloadExport")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
