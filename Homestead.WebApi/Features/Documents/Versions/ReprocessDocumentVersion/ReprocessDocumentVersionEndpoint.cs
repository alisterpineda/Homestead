using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.Versions.ReprocessDocumentVersion;

public static class ReprocessDocumentVersionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{versionId:guid}/reprocess", async (Guid documentId, Guid versionId,
            ReprocessDocumentVersionBody? body,
            ISender sender, CancellationToken ct) =>
        {
            var request = new ReprocessDocumentVersionCommand
            {
                DocumentId = documentId,
                VersionId = versionId,
                TextExtractionEngineId = body?.TextExtractionEngineId
            };
            var response = await sender.Send(request, ct);
            return Results.Accepted(null, response);
        })
        .WithName("ReprocessDocumentVersion")
        .Produces<ReprocessDocumentVersionResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
