using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.GetTextExtractionEngineSummaries;

public static class GetTextExtractionEngineSummariesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/text-extraction-engines", async (
            GetTextExtractionEngineSummariesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(new GetTextExtractionEngineSummariesRequest(), ct);
            return Results.Ok(response);
        })
        .WithName("GetTextExtractionEngineSummaries")
        .Produces<IReadOnlyList<TextExtractionEngineSummary>>()
        .RequireAuthorization(AuthPolicies.RequireViewer);
    }
}
