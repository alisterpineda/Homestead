using Homestead.WebApi.Configuration;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchDocumentSettings;

public static class PatchDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/documents", async (
            JsonPatchDocument<DocumentSettings> patchDoc,
            PatchDocumentSettingsHandler handler, CancellationToken ct) =>
        {
            var command = new PatchDocumentSettingsCommand { PatchDocument = patchDoc };
            return Results.Ok(await handler.HandleAsync(command, ct));
        })
        .WithName("PatchDocumentSettings")
        .Accepts<JsonPatchDocument<DocumentSettings>>("application/json-patch+json")
        .Produces<DocumentSettingsResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
