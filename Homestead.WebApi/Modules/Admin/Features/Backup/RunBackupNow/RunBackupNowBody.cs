namespace Homestead.WebApi.Modules.Admin.Features.Backup.RunBackupNow;

public record RunBackupNowBody
{
    /// <summary>Report the change set without copying, moving, or deleting anything.</summary>
    public bool DryRun { get; init; }

    /// <summary>Bypass the bulk-delete circuit breaker for this run only.</summary>
    public bool AllowLargePrune { get; init; }
}
