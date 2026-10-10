namespace Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;

public static class RunBackupNowEndpoint
{
    public record RunBackupNowBody
    {
        public bool DryRun { get; init; }
        public bool AllowLargePrune { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/sources/{id:guid}/runs", async (
            Guid id, RunBackupNowBody? body, RunBackupNowHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new RunBackupNowCommand
            {
                BackupSourceId = id,
                DryRun = body?.DryRun ?? false,
                AllowLargePrune = body?.AllowLargePrune ?? false,
            }, ct);

            return Results.Ok(response);
        })
        .WithName("RunBackupNow")
        .Produces<RunBackupNowResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
