namespace Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;

public static class RunBackupNowEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/sources/{id:guid}/runs", async (
            Guid id, RunBackupNowBody? body, RunBackupNowHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("RunBackupNow")
        .Produces<RunBackupNowResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
