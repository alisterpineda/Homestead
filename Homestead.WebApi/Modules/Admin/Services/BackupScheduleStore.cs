using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Documents.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Services;

public class BackupScheduleStore(AppDbContext db) : IBackupScheduleStore
{
    public async Task<ProcessingSchedule?> FindByTargetAsync(string targetKey, CancellationToken ct)
    {
        var all = await GetAllAsync(ct);
        return all.GetValueOrDefault(targetKey);
    }

    public async Task<IReadOnlyDictionary<string, ProcessingSchedule>> GetAllAsync(CancellationToken ct)
    {
        // The target key lives inside the jsonb payload, so matching happens in memory after loading the
        // BackupToTarget rows — the same shape PriceSourceScheduleStore uses. The set is one row per
        // configured target, so this stays a handful of rows however large the library gets.
        var candidates = await db.ProcessingSchedules
            .Where(s => s.JobType == ProcessingJobType.BackupToTarget)
            .ToListAsync(ct);

        var byTarget = new Dictionary<string, ProcessingSchedule>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var key = candidate.GetPayload<BackupToTargetPayload>()?.TargetKey;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            // First wins, so a duplicate row (hand-inserted, or left by an interrupted upsert) can't make
            // the list endpoint throw.
            byTarget.TryAdd(key, candidate);
        }

        return byTarget;
    }

    public async Task<bool> RemoveByTargetAsync(string targetKey, CancellationToken ct)
    {
        var existing = await FindByTargetAsync(targetKey, ct);
        if (existing is null) return false;

        db.ProcessingSchedules.Remove(existing);
        return true;
    }
}
