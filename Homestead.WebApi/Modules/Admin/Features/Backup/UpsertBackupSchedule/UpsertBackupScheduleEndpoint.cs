namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public static class UpsertBackupScheduleEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/sources/{id:guid}/schedule", async (
            Guid id, UpsertBackupScheduleBody body, UpsertBackupScheduleHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("UpsertBackupSchedule")
        .Produces<UpsertBackupScheduleResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
