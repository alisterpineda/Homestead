namespace Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;

public record ValidateBackupTargetConnectionCommand(string TargetKey);

/// <param name="StorageLimitBytes">Null means unlimited — not zero.</param>
public record ValidateBackupTargetConnectionResponse
{
    public required bool IsAvailable { get; init; }
    public string? Reason { get; init; }
    public string? ConnectedAccountEmail { get; init; }
    public long? StorageUsedBytes { get; init; }
    public long? StorageLimitBytes { get; init; }
}
