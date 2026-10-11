using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Download;

public static class DownloadDocumentsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (DownloadDocumentsCommand request, DownloadDocumentsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("DownloadDocuments")
        .Produces<DownloadDocumentsResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
