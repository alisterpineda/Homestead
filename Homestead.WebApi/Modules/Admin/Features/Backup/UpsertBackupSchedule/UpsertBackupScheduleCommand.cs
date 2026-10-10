namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public record UpsertBackupScheduleCommand
{
    public required Guid BackupSourceId { get; init; }
    public required string CronExpression { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
}

public record UpsertBackupScheduleResponse(Guid Id, DateTimeOffset NextRunAt);
