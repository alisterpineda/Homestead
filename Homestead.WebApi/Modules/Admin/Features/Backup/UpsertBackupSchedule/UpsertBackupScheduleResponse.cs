namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public record UpsertBackupScheduleResponse(Guid Id, DateTimeOffset NextRunAt);
