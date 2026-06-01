using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Homestead.WebApi.Common;

/// <summary>
/// The scoped state machine that owns a command's lazily-opened transaction. It is the shared seam
/// between <see cref="UnitOfWork"/> (which drives commit/rollback/hook draining) and the EF interceptors
/// <see cref="CommandSaveChangesInterceptor"/> / <see cref="CommandWriteCommandInterceptor"/> (which
/// lazy-open the transaction before a change-tracker-bypassing write reaches the database). Keeping the
/// transaction state here — rather than
/// on <see cref="UnitOfWork"/> — breaks the DI cycle that would otherwise form
/// (<c>AppDbContext</c> → interceptor → coordinator), because the coordinator never injects
/// <c>AppDbContext</c>; it takes the <see cref="DatabaseFacade"/> as a method parameter instead.
/// </summary>
/// <remarks>
/// This is a pure extraction of the transaction primitives that used to live on <see cref="UnitOfWork"/>;
/// the open/commit/rollback/depth semantics are unchanged. Only the owning object moved.
/// </remarks>
public class CommandTransactionCoordinator
{
    private IDbContextTransaction? _ownedTx;
    private bool _ownsTx;
    private bool _inCommandScope;
    private int _depth;
    private bool _rolledBack;

    /// <summary>
    /// Current command-frame nesting depth. Zero when no command scope is active.
    /// </summary>
    public int Depth => _depth;

    /// <summary>
    /// JOIN-or-begin entry: returns <c>true</c> if a transaction is already open on this scope (a nested
    /// <c>ISender.Send</c> joins it — the caller should just run the work and return); otherwise marks a
    /// command scope active, increments the depth, and returns <c>false</c> (this frame owns the boundary).
    /// A nested send joins only when a transaction is already open — preserving the exact lazy join/depth
    /// semantics.
    /// </summary>
    public bool TryBeginScope(DatabaseFacade db)
    {
        if (db.CurrentTransaction is not null)
            return true;

        _inCommandScope = true;
        _depth++;
        return false;
    }

    /// <summary>
    /// Lazily opens the owned command transaction on the first real write. Idempotent and scope-gated: a
    /// safe no-op outside a command scope (queries, the job-processor background scopes, the scheduler's
    /// standalone scope), when a transaction is already owned, or when one is already open on the facade.
    /// The <c>_ownedTx is not null</c> guard keeps it idempotent on InMemory (which keeps
    /// <c>CurrentTransaction</c> null after a begin); the <c>CurrentTransaction</c> clause covers a joined
    /// frame / an ambient relational transaction.
    /// </summary>
    public async Task EnsureStartedAsync(DatabaseFacade db, CancellationToken cancellationToken)
    {
        if (!_inCommandScope || _ownedTx is not null || db.CurrentTransaction is not null)
            return;

        _ownedTx = await db.BeginTransactionAsync(cancellationToken);
        _ownsTx = true;
    }

    /// <summary>
    /// Decrements the command-frame depth (guarded so a commit/hook failure that already decremented cannot
    /// drive it negative) and returns the new depth.
    /// </summary>
    public int DecrementDepth()
    {
        if (_depth > 0)
            _depth--;
        return _depth;
    }

    /// <summary>
    /// Commits the owned transaction, if this scope owns one, and releases ownership BEFORE the caller
    /// drains after-commit hooks — so a throwing hook cannot send the catch path into a rollback of the
    /// already-committed transaction.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_ownsTx)
        {
            await _ownedTx!.CommitAsync(cancellationToken);
            _ownsTx = false;
        }
    }

    /// <summary>
    /// Rolls the owned transaction back at most once across nested catch frames, swallowing a failed
    /// rollback (e.g. a broken connection) so it cannot mask the original exception. Uses
    /// <see cref="CancellationToken.None"/> so cleanup runs even when the request token is cancelled.
    /// </summary>
    public async Task RollbackOnceAsync()
    {
        if (_ownsTx && !_rolledBack)
        {
            _rolledBack = true;
            try
            {
                await _ownedTx!.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // A failed rollback must not mask the original exception that triggered it; the transaction
                // is disposed by DisposeAndResetIfTopAsync regardless.
            }
        }
    }

    /// <summary>
    /// When the outermost frame unwinds (depth back to zero), disposes the owned transaction and resets the
    /// scope flags so the next command on this scoped coordinator opens a fresh transaction.
    /// </summary>
    public async Task DisposeAndResetIfTopAsync()
    {
        if (_depth != 0)
            return;

        if (_ownedTx is not null)
        {
            await _ownedTx.DisposeAsync();
            _ownedTx = null;
            _ownsTx = false;
        }

        _inCommandScope = false;
        _rolledBack = false;
    }
}
