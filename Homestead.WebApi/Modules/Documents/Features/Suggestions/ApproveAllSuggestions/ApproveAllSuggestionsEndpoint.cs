using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Suggestions.ApproveAllSuggestions;

public static class ApproveAllSuggestionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/approve-all", async (Guid documentId,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ApproveAllSuggestionsCommand { DocumentId = documentId }, ct);
            return Results.NoContent();
        })
        .WithName("ApproveAllSuggestions")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
