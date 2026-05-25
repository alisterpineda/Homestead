using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.AiProviderSummaries.GetAiProviderSummaries;

public static class GetAiProviderSummariesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/ai-providers", async (
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetAiProviderSummariesQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetAiProviderSummaries")
        .Produces<IReadOnlyList<AiProviderSummary>>()
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
