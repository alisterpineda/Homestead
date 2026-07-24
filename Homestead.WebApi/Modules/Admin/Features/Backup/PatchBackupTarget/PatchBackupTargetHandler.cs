using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

/// <summary>
/// Updates a target's operator state: the target-level enable toggle, and nothing else. Creates the
/// state row on first use — a configured-but-never-touched target has no row.
/// </summary>
/// <remarks>
/// The prune latch is deliberately NOT here. It lives on the source row, and acknowledging it is an
/// explicit act on that one pairing (<c>PATCH /sources/{id}</c> with <c>acknowledgePruneBlock</c>):
/// clearing latches from the target would dismiss every kind's warning on that destination at once.
/// </remarks>
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
        var target = resolver.Find(request.TargetKey)
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

        state.UpdatedAt = now;

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchBackupTargetResponse(state.TargetKey, state.IsEnabled);
    }
}
