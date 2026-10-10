namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSource;

/// <param name="CancelRun">
/// Opt in to cancelling a live run so the pairing can be deleted. Without it a live run is a 409 —
/// deleting the row out from under a running mirror is not something to do by accident.
/// </param>
public record DeleteBackupSourceCommand(Guid Id, bool CancelRun = false);
