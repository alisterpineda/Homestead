using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

public record SummarizeDocumentBody(Guid? AiProviderId = null);

public static class SummarizeDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/summarize", async (Guid documentId,
            SummarizeDocumentBody? body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new SummarizeDocumentCommand
            {
                DocumentId = documentId,
                AiProviderId = body?.AiProviderId
            }, ct);
            return Results.Ok(response);
        })
        .WithName("SummarizeDocument")
        .Produces<SummarizeDocumentResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
