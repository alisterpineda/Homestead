namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;

public static class GetSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/system", async (GetSystemSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new GetSystemSettingsQuery(), ct));
        })
        .WithName("GetSystemSettings")
        .Produces<SystemSettingsResponse>();
    }
}
