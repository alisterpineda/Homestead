using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;

public static class ApproveSuggestionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{suggestionId:guid}/approve", async (Guid documentId, Guid suggestionId,
            ApproveSuggestionHandler handler, CancellationToken ct) =>
        {
            await handler.Handle(new ApproveSuggestionCommand
            {
                DocumentId = documentId,
                SuggestionId = suggestionId
            }, ct);
            return Results.NoContent();
        })
        .WithName("ApproveSuggestion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor)
        .RequireDocumentAuthorization(DocumentOperations.Write);
    }
}
