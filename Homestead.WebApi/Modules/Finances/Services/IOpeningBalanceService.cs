using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IOpeningBalanceService
{
    /// <summary>
    /// Creates an opening-balance transaction for the given account-commodity pair.
    /// Does NOT call SaveChangesAsync; the caller owns the unit of work.
    /// </summary>
    Task<AccountTransaction> CreateAsync(Guid accountId, Guid commodityId, decimal quantity,
        decimal? valuePerUnit, DateOnly asOfDate, CancellationToken ct);

    /// <summary>
    /// Updates an existing opening-balance transaction. Does NOT call SaveChangesAsync.
    /// </summary>
    Task<AccountTransaction> UpdateAsync(Guid transactionId, decimal quantity,
        decimal? valuePerUnit, DateOnly asOfDate, CancellationToken ct);

    /// <summary>
    /// Deletes an opening-balance transaction. Does NOT call SaveChangesAsync.
    /// Returns the set of <c>(account, commodity)</c> pairs the caller must pass to
    /// <see cref="IBalanceEntryReconciler"/> after the delete has been flushed, so any
    /// downstream balance entries on those chains re-reconcile against Equity:Adjustments.
    /// </summary>
    Task<IReadOnlyCollection<TouchedPair>> DeleteAsync(Guid transactionId, CancellationToken ct);
}
