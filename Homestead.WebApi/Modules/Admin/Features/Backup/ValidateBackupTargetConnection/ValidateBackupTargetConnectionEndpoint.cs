namespace Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;

public static class ValidateBackupTargetConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/validate", async (
            string key, ValidateBackupTargetConnectionHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new ValidateBackupTargetConnectionCommand(key), ct);
            return Results.Ok(response);
        })
        .WithName("ValidateBackupTargetConnection")
        .Produces<ValidateBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
