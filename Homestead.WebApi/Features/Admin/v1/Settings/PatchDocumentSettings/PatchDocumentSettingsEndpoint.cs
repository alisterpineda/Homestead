using Homestead.WebApi.Configuration;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchDocumentSettings;

public static class PatchDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/documents", async (
            JsonPatchDocument<DocumentSettings> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchDocumentSettingsCommand { PatchDocument = patchDoc };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchDocumentSettings")
        .Accepts<JsonPatchDocument<DocumentSettings>>("application/json-patch+json")
        .Produces<DocumentSettingsResponse>();
    }
}
