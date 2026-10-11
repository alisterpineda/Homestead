using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public static class DeclineSuggestionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{suggestionId:guid}/decline", async (Guid documentId, Guid suggestionId,
            DeclineSuggestionHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(documentId, suggestionId, ct);
            return Results.NoContent();
        })
        .WithName("DeclineSuggestion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
