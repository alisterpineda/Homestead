using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IOpeningBalanceService
{
    /// <summary>
    /// Creates an opening-balance transaction for the given account-commodity pair.
    /// Does NOT call SaveChangesAsync; the caller owns the unit of work.
    /// Returns the transaction plus the <see cref="TouchedPair"/>s the caller must pass to
    /// <see cref="IBalanceEntryReconciler"/> after the change is flushed — an opening balance is
    /// the earliest transaction on its (account, commodity), so inserting one shifts the
    /// prior-running of every downstream balance entry on that chain.
    /// </summary>
    Task<OpeningBalanceMutationResult> CreateAsync(CreateOpeningBalanceRequest request, CancellationToken ct);

    /// <summary>
    /// Updates an existing opening-balance transaction. Does NOT call SaveChangesAsync.
    /// Returns touched pairs covering both the old and new instants so downstream balance entries
    /// re-reconcile against Equity:Adjustments.
    /// </summary>
    Task<OpeningBalanceMutationResult> UpdateAsync(Guid transactionId, UpdateOpeningBalanceRequest request,
        CancellationToken ct);

    /// <summary>
    /// Deletes an opening-balance transaction. Does NOT call SaveChangesAsync.
    /// The result's <see cref="OpeningBalanceMutationResult.TouchedPairs"/> are the
    /// <c>(account, commodity)</c> pairs the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the delete has been flushed, so any downstream balance entries on those chains
    /// re-reconcile against Equity:Adjustments. The transaction is null (it no longer exists).
    /// </summary>
    Task<OpeningBalanceMutationResult> DeleteAsync(Guid transactionId, CancellationToken ct);
}

public record OpeningBalanceMutationResult(
    AccountTransaction? Transaction,
    IReadOnlyCollection<TouchedPair> TouchedPairs);

public record CreateOpeningBalanceRequest(
    Guid AccountId,
    Guid CommodityId,
    decimal Quantity,
    decimal? ValuePerUnit,
    DateOnly AsOfDate,
    string? Memo = null);

public record UpdateOpeningBalanceRequest(
    decimal Quantity,
    decimal? ValuePerUnit,
    DateOnly AsOfDate,
    string? Memo = null);
