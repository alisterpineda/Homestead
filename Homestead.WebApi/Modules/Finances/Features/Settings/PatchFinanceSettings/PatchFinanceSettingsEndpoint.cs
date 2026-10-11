using Homestead.WebApi.Authentication;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.PatchFinanceSettings;

public static class PatchFinanceSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("", async (
            JsonPatchDocument<FinanceSettings> patchDoc,
            PatchFinanceSettingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(patchDoc, ct)))
        .WithName("PatchFinanceSettings")
        .Accepts<JsonPatchDocument<FinanceSettings>>("application/json-patch+json")
        .Produces<FinanceSettingsResponse>()
        .RequireAuthorization(AuthPolicies.RequireAdmin);
    }
}
