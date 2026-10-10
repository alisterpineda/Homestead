namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

public record PatchBackupTargetCommand
{
    public required string TargetKey { get; init; }
    public bool? IsEnabled { get; init; }
}

public record PatchBackupTargetResponse(string TargetKey, bool IsEnabled);
