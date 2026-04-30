using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ITransferService
{
    /// <summary>
    /// Creates a Transfer transaction between two user accounts.
    /// Same-currency: both splits in source/destination's shared currency, no annotation.
    /// Cross-currency: source leg carries a price annotation bridging to the destination currency,
    /// emitting one linked CommodityPrice row.
    ///
    /// Does NOT call SaveChangesAsync. Returns the transaction plus the set of (account, commodity,
    /// earliestAffectedAt) triples the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the primary change has been flushed.
    /// </summary>
    Task<TransferMutationResult> CreateAsync(CreateTransferRequest request, CancellationToken ct);

    Task<TransferMutationResult> UpdateAsync(Guid transactionId, UpdateTransferRequest request, CancellationToken ct);

    Task<TransferMutationResult> DeleteAsync(Guid transactionId, CancellationToken ct);
}

public record CreateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal SourceAmount,
    decimal? DestinationAmount,
    DateOnly Date,
    string? Memo);

public record UpdateTransferRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal SourceAmount,
    decimal? DestinationAmount,
    DateOnly Date,
    string? Memo);

public record TransferMutationResult(
    AccountTransaction? Transaction,
    IReadOnlyCollection<TouchedPair> TouchedPairs);

public readonly record struct TouchedPair(Guid AccountId, Guid CommodityId, DateTimeOffset EarliestAffected);
