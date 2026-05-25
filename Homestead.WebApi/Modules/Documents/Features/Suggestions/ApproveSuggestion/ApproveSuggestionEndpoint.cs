using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveSuggestion;

public static class ApproveSuggestionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{suggestionId:guid}/approve", async (Guid documentId, Guid suggestionId,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ApproveSuggestionCommand
            {
                DocumentId = documentId,
                SuggestionId = suggestionId
            }, ct);
            return Results.NoContent();
        })
        .WithName("ApproveSuggestion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
