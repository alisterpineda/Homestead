using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;

public static class GetTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Mapped on the v1 group, not under /settings: that group is RequireAdmin, but the reprocess and
        // bulk-reprocess dialogs need this list too, and those are RequireContributor. Contributor is the
        // right floor — a Viewer cannot reprocess anything, so has nothing to pick an engine for.
        group.MapGet("/text-extraction-engines", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetTextExtractionEnginesQuery(), ct)))
        .WithName("GetTextExtractionEngines")
        .Produces<List<TextExtractionEngineResponse>>()
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
