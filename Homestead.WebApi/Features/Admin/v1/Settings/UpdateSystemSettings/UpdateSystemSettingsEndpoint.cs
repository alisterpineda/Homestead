namespace Homestead.WebApi.Features.Admin.v1.Settings.UpdateSystemSettings;

public static class UpdateSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/system", async (UpdateSystemSettingsRequest request,
            UpdateSystemSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("UpdateSystemSettings")
        .Produces<SystemSettingsResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
