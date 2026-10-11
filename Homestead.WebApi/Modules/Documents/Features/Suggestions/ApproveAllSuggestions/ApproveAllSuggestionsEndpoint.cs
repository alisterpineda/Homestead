using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;

public static class ApproveAllSuggestionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/approve-all", async (Guid documentId,
            ApproveAllSuggestionsHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(documentId, ct);
            return Results.NoContent();
        })
        .WithName("ApproveAllSuggestions")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
