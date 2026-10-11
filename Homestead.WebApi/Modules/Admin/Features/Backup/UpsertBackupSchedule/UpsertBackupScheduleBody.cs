namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public record UpsertBackupScheduleBody
{
    public required string CronExpression { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
}
