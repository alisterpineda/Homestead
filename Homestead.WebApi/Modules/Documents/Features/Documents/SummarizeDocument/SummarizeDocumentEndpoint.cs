using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

// Kept as an empty record so the POST still accepts an (ignored) body without a 415/400 regression.
public record SummarizeDocumentBody();

public static class SummarizeDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/summarize", async (Guid documentId,
            SummarizeDocumentBody? body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new SummarizeDocumentCommand
            {
                DocumentId = documentId
            }, ct);
            return Results.Ok(response);
        })
        .WithName("SummarizeDocument")
        .Produces<SummarizeDocumentResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
