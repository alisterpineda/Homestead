namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public static class PatchSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/system", async (PatchSystemSettingsCommand request,
            PatchSystemSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("PatchSystemSettings")
        .Produces<SystemSettingsResponse>();
    }
}
