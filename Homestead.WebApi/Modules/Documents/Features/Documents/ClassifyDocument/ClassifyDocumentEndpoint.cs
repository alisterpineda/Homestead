using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public static class ClassifyDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/classify", async (Guid documentId,
            ClassifyDocumentBody? body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ClassifyDocumentCommand
            {
                DocumentId = documentId,
                AiProviderId = body?.AiProviderId
            }, ct);
            return Results.Accepted(null, response);
        })
        .WithName("ClassifyDocument")
        .Produces<ClassifyDocumentResponse>(StatusCodes.Status202Accepted)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}

public record ClassifyDocumentBody(Guid? AiProviderId = null);
