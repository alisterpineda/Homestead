namespace Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;

public static class ValidateBackupTargetConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/validate", async (
            string key, ValidateBackupTargetConnectionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(key, ct)))
        .WithName("ValidateBackupTargetConnection")
        .Produces<ValidateBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
