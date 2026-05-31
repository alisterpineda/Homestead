using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    private IDbContextTransaction? _ownedTx;
    private bool _ownsTx;
    private bool _inCommandScope;
    private int _depth;
    private bool _rolledBack;

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        // JOIN: a nested ISender.Send inside an already-open ambient transaction joins it and returns.
        // The outermost frame owns commit + hook draining. No depth/scope mutation on the join path.
        if (db.Database.CurrentTransaction is not null)
            return await work();

        _inCommandScope = true;
        _depth++;
        try
        {
            var result = await work();
            // Lazy: SaveChangesAsync opens the tx on the first real flush (after its idempotent guard),
            // so slow non-DB work in the handler holds no pooled connection.
            await SaveChangesAsync(cancellationToken);

            if (--_depth == 0)
            {
                if (_ownsTx)
                {
                    await _ownedTx!.CommitAsync(cancellationToken);
                    // Committed: release ownership BEFORE draining hooks so a throwing after-commit hook
                    // cannot send the catch into a rollback of the already-committed transaction.
                    _ownsTx = false;
                }
                await RunAfterCommitHooksAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            if (_depth > 0)
                _depth--;
            // Roll back at most once across nested catch frames; CancellationToken.None so cleanup runs
            // even when the request token is cancelled.
            if (_ownsTx && !_rolledBack)
            {
                _rolledBack = true;
                try
                {
                    await _ownedTx!.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // A failed rollback (e.g. a broken connection) must not mask the original exception
                    // that triggered it; the transaction is disposed in the finally regardless.
                }
            }

            throw;
        }
        finally
        {
            if (_depth == 0 && _ownedTx is not null)
            {
                await _ownedTx.DisposeAsync();
                _ownedTx = null;
                _ownsTx = false;
            }

            if (_depth == 0)
            {
                _inCommandScope = false;
                _rolledBack = false;
                // Discard any hooks a rolled-back command registered so they cannot leak into the next
                // command that reuses this scoped unit of work (the success path already drained them).
                _afterCommitHooks = null;
            }
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // Idempotent: nothing tracked + no pending audit => true no-op, and (critically) opens no tx.
        // The audit flush below adds rows to the change tracker, so this guard is what makes a second
        // SaveChangesAsync (e.g. from the behavior) a true no-op.
        if (!auditContext.HasEntries && !db.ChangeTracker.HasChanges())
            return 0;

        // Lazy-open the command transaction on the first real flush.
        await EnsureTransactionStartedCoreAsync(cancellationToken);

        // Flush batched audit through the existing actor-threading seam. The actor is the request's
        // explicit ActorUserId when supplied (internal jobs), otherwise the signed-in user.
        var actor = _actorOverride ?? currentUser.UserId;
        auditContext.FlushTo(auditLogger.ForUser(actor));

        return await db.SaveChangesAsync(cancellationToken);
    }

    public Task EnsureTransactionAsync(CancellationToken cancellationToken)
        => EnsureTransactionStartedCoreAsync(cancellationToken);

    public void OnAfterCommit(Func<CancellationToken, Task> hook)
    {
        (_afterCommitHooks ??= []).Add(hook);
    }

    public void SetActor(Guid? actorUserId) => _actorOverride = actorUserId;

    private async Task EnsureTransactionStartedCoreAsync(CancellationToken cancellationToken)
    {
        // Safe no-op outside a command scope (queries / a direct SaveChangesAsync with no ExecuteInTransaction
        // frame). `_ownedTx is not null` makes this idempotent on InMemory (which keeps CurrentTransaction
        // null after begin); the CurrentTransaction clause covers a joined frame / an ambient relational tx.
        if (!_inCommandScope || _ownedTx is not null || db.Database.CurrentTransaction is not null)
            return;

        _ownedTx = await db.Database.BeginTransactionAsync(cancellationToken);
        _ownsTx = true;
    }

    private async Task RunAfterCommitHooksAsync(CancellationToken cancellationToken)
    {
        if (_afterCommitHooks is null)
            return;

        var hooks = _afterCommitHooks;
        _afterCommitHooks = null;
        foreach (var hook in hooks)
            await hook(cancellationToken);
    }
}
