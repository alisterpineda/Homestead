namespace Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;

public static class PollBackupTargetConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect/poll", async (
            string key, PollBackupTargetConnectionBody body, PollBackupTargetConnectionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(key, body, ct)))
        .WithName("PollBackupTargetConnection")
        .Produces<PollBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
