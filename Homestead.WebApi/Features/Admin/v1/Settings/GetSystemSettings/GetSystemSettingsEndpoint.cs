namespace Homestead.WebApi.Features.Admin.v1.Settings.GetSystemSettings;

public static class GetSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/system", (GetSystemSettingsHandler handler) =>
        {
            return Results.Ok(handler.Handle());
        })
        .WithName("GetSystemSettings")
        .Produces<SystemSettingsResponse>();
    }
}
