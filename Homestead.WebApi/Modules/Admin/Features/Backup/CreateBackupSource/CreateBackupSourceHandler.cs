using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.CreateBackupSource;

/// <summary>
/// Pairs a code-registered source kind with a config-defined target. This row is what everything else
/// hangs off — runs, schedules, state, the breaker latch.
/// </summary>
public class CreateBackupSourceHandler(
    AppDbContext db,
    IBackupSourceCatalog catalog,
    IBackupTargetResolver resolver,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task<CreateBackupSourceResponse> HandleAsync(
        CreateBackupSourceBody body, CancellationToken ct)
    {
        // An unregistered kind is a bad request, not a missing resource: the caller sent a value outside
        // the enumerable set the picker offers.
        var descriptor = catalog.Find(body.SourceKind)
            ?? throw new BusinessRuleException(
                $"'{body.SourceKind}' is not a backup source kind. " +
                $"Known kinds: {string.Join(", ", catalog.All().Select(d => d.Key))}.");

        // A target that is DEFINED but unavailable is still pairable — an operator sets up the mirror
        // while the NAS is still being mounted, and run-now / schedule-upsert / the planner are the
        // enforcement points. An UNDEFINED key is a 404: config is the only thing that makes a target
        // exist, so there is nothing to pair with.
        var target = resolver.Find(body.TargetKey)
            ?? throw new EntityNotFoundException($"Backup target '{body.TargetKey}' is not configured.");

        // Friendly pre-check for the common case. The real guarantee is the expression unique index on
        // (SourceKind, lower(TargetKey)) — this read-then-write is racy on its own, and the index is what
        // stops two concurrent creates producing a pair of rows that would prune each other's mirrors.
        var duplicate = await db.BackupSources.AnyAsync(s =>
            s.SourceKind == descriptor.Key && s.TargetKey.ToLower() == target.Key.ToLower(), ct);

        if (duplicate)
            throw new DuplicateEntityException(
                $"'{descriptor.DisplayName}' is already backed up to '{target.Key}'.");

        var now = DateTimeOffset.UtcNow;

        var source = new BackupSource
        {
            SourceKind = descriptor.Key,
            // The RESOLVER's casing, not the caller's: the row and config must agree on how the target is
            // spelled, or every subsequent display reads back whatever the first admin happened to type.
            TargetKey = target.Key,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.BackupSources.Add(source);
        audit.Created("BackupSource", source.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return new CreateBackupSourceResponse(source.Id, source.SourceKind, source.TargetKey);
    }
}
