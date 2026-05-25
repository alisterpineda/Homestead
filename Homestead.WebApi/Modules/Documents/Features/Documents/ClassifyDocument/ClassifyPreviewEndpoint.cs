using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public static class ClassifyPreviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/classify-preview", async (Guid documentId,
            ClassifyDocumentBody? body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ClassifyPreviewQuery
            {
                DocumentId = documentId,
                AiProviderId = body?.AiProviderId
            }, ct);
            return Results.Ok(response);
        })
        .WithName("ClassifyPreview")
        .Produces<ClassifyPreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
