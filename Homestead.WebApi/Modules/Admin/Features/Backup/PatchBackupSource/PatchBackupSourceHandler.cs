using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;

/// <summary>
/// Updates one pairing's operator state: enable/suspend, and acknowledge the bulk-delete breaker latch.
/// </summary>
public class PatchBackupSourceHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<PatchBackupSourceResponse> HandleAsync(
        Guid id, PatchBackupSourceBody body, CancellationToken ct)
    {
        var source = await db.BackupSources.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new EntityNotFoundException("Backup source not found.");

        audit.Track<BackupSourceSnapshot, Domain.Backup.BackupSource>(
            source, "BackupSource", source.Id);

        if (body.IsEnabled is { } isEnabled)
            source.IsEnabled = isEnabled;

        // Only an explicit acknowledgement clears the latch — see PatchBackupSourceBody.AcknowledgePruneBlock.
        if (body.AcknowledgePruneBlock is true)
        {
            source.PruneBlockedAt = null;
            source.PruneBlockedCount = null;
        }

        source.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchBackupSourceResponse(source.Id, source.IsEnabled, source.PruneBlockedAt);
    }
}
