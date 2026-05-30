using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Common;

/// <inheritdoc cref="IUnitOfWork"/>
public class UnitOfWork(
    AppDbContext db,
    IAuditContext auditContext,
    IAuditLogger auditLogger,
    ICurrentUserAccessor currentUser) : IUnitOfWork
{
    private Guid? _actorOverride;
    private List<Func<CancellationToken, Task>>? _afterCommitHooks;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Idempotent: when a handler already committed (or there is simply nothing to do), neither
        // re-commit nor re-flush audit. The audit flush below adds rows to the change tracker, so this
        // guard is what makes a second SaveChangesAsync (e.g. from the behavior) a true no-op.
        if (!auditContext.HasEntries && !db.ChangeTracker.HasChanges())
            return 0;

        // Flush batched audit through the existing actor-threading seam. The actor is the request's
        // explicit ActorUserId when supplied (internal jobs), otherwise the signed-in user.
        var actor = _actorOverride ?? currentUser.UserId;
        auditContext.FlushTo(auditLogger.ForUser(actor));

        return await db.SaveChangesAsync(cancellationToken);
    }

    public void OnAfterCommit(Func<CancellationToken, Task> hook)
    {
        (_afterCommitHooks ??= []).Add(hook);
    }

    public void SetActor(Guid? actorUserId) => _actorOverride = actorUserId;

    public async Task RunAfterCommitHooksAsync(CancellationToken cancellationToken)
    {
        if (_afterCommitHooks is null)
            return;

        var hooks = _afterCommitHooks;
        _afterCommitHooks = null;
        foreach (var hook in hooks)
            await hook(cancellationToken);
    }
}
