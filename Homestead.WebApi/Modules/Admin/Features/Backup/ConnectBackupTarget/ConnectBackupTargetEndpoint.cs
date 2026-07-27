using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;

public static class ConnectBackupTargetEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect", async (
            string key, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ConnectBackupTargetCommand(key), ct);
            return Results.Ok(response);
        })
        .WithName("ConnectBackupTarget")
        .Produces<ConnectBackupTargetResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
