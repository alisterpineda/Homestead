using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineSuggestion;

public static class DeclineSuggestionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{suggestionId:guid}/decline", async (Guid documentId, Guid suggestionId,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeclineSuggestionCommand
            {
                DocumentId = documentId,
                SuggestionId = suggestionId
            }, ct);
            return Results.NoContent();
        })
        .WithName("DeclineSuggestion")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
