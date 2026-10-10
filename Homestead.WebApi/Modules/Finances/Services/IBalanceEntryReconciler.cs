namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Re-derives the reconciling splits on <c>BalanceEntryTransaction</c>s whose
/// <c>(account, commodity)</c> chain has been touched by some other mutation.
///
/// Run this AFTER <c>SaveChangesAsync</c> has flushed the primary change so the reconciler's
/// DB queries see the updated state. The reconciler recomputes each affected balance entry's
/// delta from current split state and rewrites its two splits in place (preserving <c>Id</c>s),
/// auditing each as Updated. Prior-running is computed from raw <c>Quantity</c> in the asserted
/// commodity — no display-currency conversion at reconciliation time.
///
/// The reconciler does NOT call <c>SaveChangesAsync</c>; the enclosing command transaction
/// (<c>CommandTransactionFilter</c> via <c>IUnitOfWork.ExecuteInTransactionAsync</c>) flushes the
/// audit entries and split modifications at the end of the request.
/// </summary>
public interface IBalanceEntryReconciler
{
    Task ReconcileAsync(IEnumerable<TouchedPair> touched, CancellationToken ct);
}
