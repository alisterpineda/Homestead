namespace Homestead.WebApi.Modules.Admin.Features.Backup.ConfirmBackupTargetConnection;

/// <param name="ResetCopyCount">How many recorded copies were forgotten, for the confirmation toast.</param>
public record ConfirmBackupTargetConnectionResponse(
    string TargetKey,
    string? ConnectedAccountEmail,
    int ResetCopyCount);
