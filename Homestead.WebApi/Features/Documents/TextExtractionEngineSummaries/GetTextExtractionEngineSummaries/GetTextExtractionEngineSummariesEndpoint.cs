using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

public static class GetTextExtractionEngineSummariesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/text-extraction-engines", async (
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetTextExtractionEngineSummariesQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetTextExtractionEngineSummaries")
        .Produces<IReadOnlyList<TextExtractionEngineSummary>>()
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
