namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConfirmBackupTargetConnection;

public static class ConfirmBackupTargetConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect/confirm", async (
            string key, ConfirmBackupTargetConnectionBody body, ConfirmBackupTargetConnectionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(key, body, ct)))
        .WithName("ConfirmBackupTargetConnection")
        .Produces<ConfirmBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
