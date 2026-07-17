using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IFlowService
{
    /// <summary>
    /// Creates an ordinary spend/income flow on a non-Investment account. Two legs in the account's
    /// reporting currency: the user-account leg and the system Expense/Income counterpart leg. Signs
    /// follow flow direction (Expense: −a on the account, +a on Expense; Income: +a on the account,
    /// −a on Income), so one code path works uniformly on asset and liability accounts — no sign flip.
    ///
    /// Does NOT call SaveChangesAsync. Returns the transaction plus the (account, commodity,
    /// earliestAffectedAt) triples the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the primary change has been flushed.
    /// </summary>
    Task<FlowMutationResult> CreateAsync(CreateFlowRequest request, CancellationToken ct);

    Task<FlowMutationResult> UpdateAsync(Guid transactionId, UpdateFlowRequest request, CancellationToken ct);
}

public record CreateFlowRequest(
    FlowDirection Direction,
    Guid AccountId,
    decimal Amount,
    DateOnly Date,
    string? Description,
    Guid? CategoryId = null,
    Guid? PayeeId = null);

// No Direction: a flow's direction is immutable (fixed by the stored shape) — the patch cannot flip it.
// CategoryId can change: recategorising re-resolves the counterpart leg (see FlowService.UpdateAsync).
public record UpdateFlowRequest(
    Guid AccountId,
    decimal Amount,
    DateOnly Date,
    string? Description,
    Guid? CategoryId = null,
    Guid? PayeeId = null);

public record FlowMutationResult(
    AccountTransaction? Transaction,
    IReadOnlyCollection<TouchedPair> TouchedPairs);
