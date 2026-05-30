namespace Homestead.WebApi.Common;

/// <summary>
/// The single persistence/audit/transaction chokepoint for a request scope. Wraps the EF
/// <c>AppDbContext</c>, the batched <see cref="Auditing.IAuditContext"/>, and actor resolution so
/// audit behavior cannot drift across the many document call sites, and owns the transaction boundary
/// for a command: a unit of work <em>is</em> a transactional boundary.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExecuteInTransactionAsync{T}"/> wraps a command's handler in a single database
/// transaction that commits exactly once at the end of the pipeline (driven by
/// <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/>). Any failure rolls everything back.
/// </para>
/// <para>
/// <see cref="SaveChangesAsync"/> is the only place persistence and audit are flushed together, so it
/// stays correct no matter how many times it is called. Inside a command transaction it merely
/// <em>flushes</em> — read-your-writes works for follow-up reads, but nothing is durable until the
/// transaction commits. It is idempotent, so handlers that flush mid-handler and the final commit
/// never double-write or double-audit.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a single database transaction, flushes persistence and audit
    /// with <see cref="SaveChangesAsync"/>, commits once, then drains <see cref="OnAfterCommit"/> hooks
    /// <em>outside</em> the transaction. If a transaction is already open on this scope (a nested command
    /// dispatched through <c>ISender</c>), it joins the ambient transaction instead of opening a new one —
    /// the outermost command owns the commit and hook draining.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the audit actor, flushes batched audit entries against the EF change tracker, then
    /// persists everything in one <c>SaveChanges</c>. Idempotent: a no-op (returns 0) when there are
    /// no tracked changes and no pending audit entries, so a handler that already flushed is neither
    /// double-committed nor double-audited. Inside a command transaction this flushes rather than
    /// commits — durability comes from the transaction commit in <see cref="ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues a side effect (SignalR notifications, file cleanup, thumbnails, job-processor signals)
    /// to run only after the work has durably committed. Hooks are drained by
    /// <see cref="ExecuteInTransactionAsync{T}"/> once the command's commit has completed, outside the
    /// transaction.
    /// </summary>
    void OnAfterCommit(Func<CancellationToken, Task> hook);

    /// <summary>
    /// Sets the actor audit entries are attributed to for this scope, overriding the ambient signed-in
    /// user. Set by <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> from an
    /// <see cref="IActorAttributed"/> request; a null value falls back to the current user.
    /// </summary>
    void SetActor(Guid? actorUserId);
}
