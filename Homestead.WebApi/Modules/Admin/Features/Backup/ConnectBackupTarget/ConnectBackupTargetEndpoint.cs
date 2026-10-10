namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;

public static class ConnectBackupTargetEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect", async (
            string key, ConnectBackupTargetHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new ConnectBackupTargetCommand(key), ct);
            return Results.Ok(response);
        })
        .WithName("ConnectBackupTarget")
        .Produces<ConnectBackupTargetResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
