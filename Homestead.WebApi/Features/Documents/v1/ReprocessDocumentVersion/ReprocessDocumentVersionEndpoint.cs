using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;

public static class ReprocessDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{versionId:guid}/reprocess", async (Guid documentId, Guid versionId,
            ReprocessDocumentVersionBody? body,
            ReprocessDocumentVersionHandler handler, CancellationToken ct) =>
        {
            var request = new ReprocessDocumentVersionRequest
            {
                DocumentId = documentId,
                VersionId = versionId,
                TextExtractionEngineId = body?.TextExtractionEngineId
            };
            var response = await handler.HandleAsync(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("ReprocessDocumentVersion")
        .Produces<ReprocessDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
