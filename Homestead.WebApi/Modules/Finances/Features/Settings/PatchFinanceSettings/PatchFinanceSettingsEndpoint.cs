using Homestead.WebApi.Authentication;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public static class PatchFinanceSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("", async (
            JsonPatchDocument<FinanceSettings> patchDoc,
            ISender sender, CancellationToken ct) =>
        {
            var command = new PatchFinanceSettingsCommand { PatchDocument = patchDoc };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("PatchFinanceSettings")
        .Accepts<JsonPatchDocument<FinanceSettings>>("application/json-patch+json")
        .Produces<FinanceSettingsResponse>()
        .RequireAuthorization(AuthPolicies.RequireAdmin);
    }
}
