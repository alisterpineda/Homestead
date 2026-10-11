using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public static class DeclineAllSuggestionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/decline-all", async (Guid documentId,
            DeclineAllSuggestionsHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(documentId, ct);
            return Results.NoContent();
        })
        .WithName("DeclineAllSuggestions")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
