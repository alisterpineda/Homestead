using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Services;

/// <summary>
/// Finds and removes the <c>ProcessingSchedule</c> row that drives one backup target's recurring run.
/// Backup schedules live in the shared <c>processing.schedules</c> table like every other schedule; what
/// identifies them is <c>JobType = BackupToTarget</c> plus a payload naming the target.
/// </summary>
public interface IBackupScheduleStore
{
    Task<ProcessingSchedule?> FindByTargetAsync(string targetKey, CancellationToken ct);

    /// <summary>
    /// One schedule per target key, in stable key order. One query, for the list endpoint — calling
    /// <see cref="FindByTargetAsync"/> per target would be N reads of the same table.
    /// </summary>
    Task<IReadOnlyDictionary<string, ProcessingSchedule>> GetAllAsync(CancellationToken ct);

    /// <summary>Marks the target's schedule for deletion. Returns false when there was none.</summary>
    Task<bool> RemoveByTargetAsync(string targetKey, CancellationToken ct);
}
