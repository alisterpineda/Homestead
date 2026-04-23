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
    /// </summary>
    Task DeleteAsync(Guid transactionId, CancellationToken ct);
}
