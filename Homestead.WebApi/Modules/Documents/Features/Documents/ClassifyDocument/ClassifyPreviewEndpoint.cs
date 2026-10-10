using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyDocumentBody(List<DocumentSuggestionField>? Fields = null);

public static class ClassifyPreviewEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/classify-preview", async (Guid documentId,
            ClassifyDocumentBody? body, ClassifyPreviewHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new ClassifyPreviewQuery
            {
                DocumentId = documentId,
                Fields = body?.Fields
            }, ct);
            return Results.Ok(response);
        })
        .WithName("ClassifyPreview")
        .Produces<ClassifyPreviewResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
