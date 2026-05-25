using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.DeclineAllSuggestions;

public static class DeclineAllSuggestionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/decline-all", async (Guid documentId,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeclineAllSuggestionsCommand { DocumentId = documentId }, ct);
            return Results.NoContent();
        })
        .WithName("DeclineAllSuggestions")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
