using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public static class ClassifyPreviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/classify-preview", async (Guid documentId,
            ClassifyDocumentBody? body, ClassifyPreviewHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(documentId, body, ct)))
        .WithName("ClassifyPreview")
        .Produces<ClassifyPreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
