namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

public record PatchBackupTargetResponse(string TargetKey, bool IsEnabled);
