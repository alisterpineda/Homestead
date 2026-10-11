namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

public record PatchBackupTargetBody
{
    public bool? IsEnabled { get; init; }
}
