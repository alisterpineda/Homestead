using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public static class PatchSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/system", async (PatchSystemSettingsCommand request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("PatchSystemSettings")
        .Produces<SystemSettingsResponse>();
    }
}
