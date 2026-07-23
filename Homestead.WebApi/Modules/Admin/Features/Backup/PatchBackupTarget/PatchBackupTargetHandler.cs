using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

/// <summary>
/// Updates a target's operator state and acknowledges the prune latch. Creates the state row on first
/// use — a configured-but-never-touched target has no row.
/// </summary>
public class PatchBackupTargetHandler(
    AppDbContext db, IBackupTargetResolver resolver, IUnitOfWork unitOfWork)
    : IRequestHandler<PatchBackupTargetCommand, PatchBackupTargetResponse>
{
    public async Task<PatchBackupTargetResponse> Handle(
        PatchBackupTargetCommand request, CancellationToken ct)
    {
        // Config defines what a target IS, so an undefined key is a 404 even though a state row could be
        // written for any string. Without this, a typo would create orphan state the admin page never
        // shows and nothing ever cleans up. A DEFINED but misconfigured target is still patchable —
        // disabling one, or acknowledging its latch, is exactly what an operator does while fixing it.
        var target = resolver.All()
            .FirstOrDefault(t => string.Equals(t.Key, request.TargetKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new EntityNotFoundException($"Backup target '{request.TargetKey}' is not configured.");

        var now = DateTimeOffset.UtcNow;

        var state = await db.BackupTargetStates
            .FirstOrDefaultAsync(s => s.TargetKey == target.Key, ct);

        if (state is null)
        {
            state = new BackupTargetState
            {
                TargetKey = target.Key,
                IsEnabled = true,
                CreatedAt = now,
            };
            db.BackupTargetStates.Add(state);
        }

        if (request.IsEnabled is { } isEnabled)
            state.IsEnabled = isEnabled;

        // Any PATCH acknowledges the prune latch. This is the admin's "I've seen it" action — the banner
        // it drives is the only place a blocked prune is reported, so it must take a deliberate act to
        // clear, and a deliberate act must actually clear it.
        state.PruneBlockedAt = null;
        state.PruneBlockedCount = null;
        state.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchBackupTargetResponse(state.TargetKey, state.IsEnabled);
    }
}
