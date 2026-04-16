using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class DownloadDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (DownloadDocumentsCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("DownloadDocuments")
        .Produces<DownloadDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
