namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchSystemSettings;

public static class PatchSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/system", async (PatchSystemSettingsRequest request,
            PatchSystemSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("PatchSystemSettings")
        .Produces<SystemSettingsResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
