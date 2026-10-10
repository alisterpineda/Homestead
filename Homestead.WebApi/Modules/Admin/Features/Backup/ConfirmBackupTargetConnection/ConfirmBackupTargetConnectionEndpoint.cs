namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConfirmBackupTargetConnection;

public static class ConfirmBackupTargetConnectionEndpoint
{
    public record ConfirmBackupTargetConnectionBody(string PendingToken);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect/confirm", async (
            string key, ConfirmBackupTargetConnectionBody body, ConfirmBackupTargetConnectionHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(
                new ConfirmBackupTargetConnectionCommand(key, body.PendingToken), ct);

            return Results.Ok(response);
        })
        .WithName("ConfirmBackupTargetConnection")
        .Produces<ConfirmBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
