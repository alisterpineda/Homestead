using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ITradeService
{
    /// <summary>
    /// Creates an investment Buy or Sell. Both legs sit on the same investment account: a cash leg
    /// in the funding currency (signed by side — Buy spends cash, Sell receives it) and a security
    /// leg in the traded commodity, annotated with the per-unit price. The annotation materialises
    /// one linked CommodityPrice row at (security, funding, OccurredAt).
    ///
    /// Does NOT call SaveChangesAsync. Returns the transaction plus the (account, commodity,
    /// earliestAffectedAt) triples the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the primary change has been flushed.
    /// </summary>
    Task<TradeMutationResult> CreateAsync(CreateTradeRequest request, CancellationToken ct);

    Task<TradeMutationResult> UpdateAsync(Guid transactionId, UpdateTradeRequest request, CancellationToken ct);
}

public record CreateTradeRequest(
    Guid AccountId,
    TradeSide Side,
    Guid SecurityId,
    Guid FundingCurrencyId,
    decimal Quantity,
    decimal UnitPrice,
    DateTimeOffset OccurredAt,
    string? Memo);

public record UpdateTradeRequest(
    Guid AccountId,
    TradeSide Side,
    Guid SecurityId,
    Guid FundingCurrencyId,
    decimal Quantity,
    decimal UnitPrice,
    DateTimeOffset OccurredAt,
    string? Memo);

public record TradeMutationResult(
    AccountTransaction? Transaction,
    IReadOnlyCollection<TouchedPair> TouchedPairs);
