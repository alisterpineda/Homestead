using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSchedule;

public record DeleteBackupScheduleCommand(string TargetKey) : ICommand<bool>;
