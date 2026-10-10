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
        {
            var request = new ReprocessDocumentVersionCommand
            {
                DocumentId = documentId,
                VersionId = versionId,
                // body?. — an omitted body has always been legal here and must keep meaning
                // "use the configured default", not "no engine".
                EngineKey = body?.EngineKey
            };
            var response = await handler.Handle(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("ReprocessDocumentVersion")
        .Produces<ReprocessDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
