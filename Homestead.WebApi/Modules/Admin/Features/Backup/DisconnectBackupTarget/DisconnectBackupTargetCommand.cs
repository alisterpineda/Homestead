using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DisconnectBackupTarget;

public record DisconnectBackupTargetCommand(string TargetKey) : ICommand<bool>;
