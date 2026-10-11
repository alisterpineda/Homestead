namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;

public static class ConnectBackupTargetEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect", async (
            string key, ConnectBackupTargetHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(key, ct)))
        .WithName("ConnectBackupTarget")
        .Produces<ConnectBackupTargetResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
