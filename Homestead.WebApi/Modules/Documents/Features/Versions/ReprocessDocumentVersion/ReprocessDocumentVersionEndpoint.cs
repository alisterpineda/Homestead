using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

public static class ReprocessDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{versionId:guid}/reprocess", async (Guid documentId, Guid versionId,
            ReprocessDocumentVersionBody? body,
            ReprocessDocumentVersionHandler handler, CancellationToken ct) =>
            Results.Accepted(null, await handler.HandleAsync(documentId, versionId, body, ct)))
        .WithName("ReprocessDocumentVersion")
        .Produces<ReprocessDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
