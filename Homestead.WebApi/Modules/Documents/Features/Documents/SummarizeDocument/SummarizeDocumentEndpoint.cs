using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.SummarizeDocument;

// Kept as an empty record so the POST still accepts an (ignored) body without a 415/400 regression.
public record SummarizeDocumentBody();

// The one sanctioned slow synchronous AI handler. The frontend summarizes through the bulk job, but this route is
// kept because IdleInTransactionTests uses it as the acceptance test that a slow AI call before the first flush
// holds no connection idle-in-transaction (UnmatchedApiRouteTests exercises it too). It classifies text-only
// (attachImage: false), so once PDF input mode is on its summary may differ from the job path's.
public static class SummarizeDocumentEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{documentId:guid}/summarize", async (Guid documentId,
            SummarizeDocumentBody? body, SummarizeDocumentHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(documentId, ct)))
        .WithName("SummarizeDocument")
        .Produces<SummarizeDocumentResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
