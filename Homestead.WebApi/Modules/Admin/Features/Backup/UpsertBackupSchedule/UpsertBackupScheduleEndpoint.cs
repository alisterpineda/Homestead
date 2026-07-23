using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public static class UpsertBackupScheduleEndpoint
{
    public record UpsertBackupScheduleBody
    {
        public required string CronExpression { get; init; }
        public bool IsEnabled { get; init; } = true;
        public int Priority { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/targets/{key}/schedule", async (
            string key, UpsertBackupScheduleBody body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new UpsertBackupScheduleCommand
            {
                TargetKey = key,
                CronExpression = body.CronExpression,
                IsEnabled = body.IsEnabled,
                Priority = body.Priority,
            }, ct);

            return Results.Ok(response);
        })
        .WithName("UpsertBackupSchedule")
        .Produces<UpsertBackupScheduleResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
