using MediatR;

namespace Homestead.WebApi.Features.Admin.Settings.GetSystemSettings;

public static class GetSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/system", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetSystemSettingsQuery(), ct));
        })
        .WithName("GetSystemSettings")
        .Produces<SystemSettingsResponse>();
    }
}
