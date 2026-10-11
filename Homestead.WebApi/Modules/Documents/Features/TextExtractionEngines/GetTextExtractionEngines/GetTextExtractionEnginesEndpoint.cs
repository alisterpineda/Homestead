using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

public static class GetTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Mapped on the v1 group, not under /settings: that group is RequireAdmin, but the reprocess and
        // bulk-reprocess dialogs need this list too, and those are RequireContributor. Contributor is the
        // right floor — a Viewer cannot reprocess anything, so has nothing to pick an engine for.
        group.MapGet("/text-extraction-engines", async (GetTextExtractionEnginesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetTextExtractionEngines")
        .Produces<List<TextExtractionEngineResponse>>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
