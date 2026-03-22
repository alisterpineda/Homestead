using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchSystemSettings;

public static class PatchSystemSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/system", async (PatchSystemSettingsRequest request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("PatchSystemSettings")
        .Produces<SystemSettingsResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
