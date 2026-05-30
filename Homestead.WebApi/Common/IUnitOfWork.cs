namespace Homestead.WebApi.Common;

/// <summary>
/// The single persistence/audit chokepoint for a request scope. Wraps the EF
/// <c>AppDbContext</c>, the batched <see cref="Auditing.IAuditContext"/>, and actor resolution so
/// audit behavior cannot drift across the many document call sites.
/// </summary>
/// <remarks>
/// <see cref="SaveChangesAsync"/> is the only place persistence and audit are flushed together, so
/// it stays correct no matter how many times it is called. Handlers that build their response from
/// post-save reads call it themselves; <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> calls it
/// once more after the handler returns (idempotent — a no-op when nothing is pending).
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Resolves the audit actor, flushes batched audit entries against the EF change tracker, then
    /// persists everything in one <c>SaveChanges</c>. Idempotent: a no-op (returns 0) when there are
    /// no tracked changes and no pending audit entries, so a handler that already saved is neither
    /// double-committed nor double-audited.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a side effect (SignalR notifications, file cleanup, thumbnails) to run only after the
    /// work has durably committed. Hooks are drained by <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/>
    /// once the command's commit has completed.
    /// </summary>
    void OnAfterCommit(Func<CancellationToken, Task> hook);

    /// <summary>
    /// Sets the actor audit entries are attributed to for this scope, overriding the ambient signed-in
    /// user. Set by <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> from an
    /// <see cref="IActorAttributed"/> request; a null value falls back to the current user.
    /// </summary>
    void SetActor(Guid? actorUserId);

    /// <summary>
    /// Runs and clears any hooks registered via <see cref="OnAfterCommit"/>. Called by
    /// <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> after the command commit.
    /// </summary>
    Task RunAfterCommitHooksAsync(CancellationToken cancellationToken);
}
