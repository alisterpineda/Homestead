using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;

public static class PatchDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("", async (
            JsonPatchDocument<DocumentSettings> patchDoc,
            PatchDocumentSettingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(patchDoc, ct)))
        .WithName("PatchDocumentSettings")
        .Accepts<JsonPatchDocument<DocumentSettings>>("application/json-patch+json")
        .Produces<DocumentSettingsResponse>();
    }
}
