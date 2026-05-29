using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Documents;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyDocumentBody(Guid? AiProviderId = null, List<DocumentSuggestionField>? Fields = null);

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
                AiProviderId = body?.AiProviderId,
                Fields = body?.Fields
            }, ct);
            return Results.Ok(response);
        })
        .WithName("ClassifyPreview")
        .Produces<ClassifyPreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
