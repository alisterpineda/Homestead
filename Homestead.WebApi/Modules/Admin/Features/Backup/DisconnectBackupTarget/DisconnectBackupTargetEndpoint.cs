using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DisconnectBackupTarget;

public static class DisconnectBackupTargetEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/targets/{key}/connection", async (
            string key, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DisconnectBackupTargetCommand(key), ct);
            return Results.NoContent();
        })
        .WithName("DisconnectBackupTarget")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}
