using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IDividendService
{
    /// <summary>
    /// Creates a cash Dividend on an investment account. Two legs in the payment currency: the
    /// investment-account leg is positive (cash in), the system Income leg is negative. No price
    /// annotation, so no linked CommodityPrice row.
    ///
    /// Does NOT call SaveChangesAsync. Returns the transaction plus the (account, commodity,
    /// earliestAffectedAt) triples the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the primary change has been flushed.
    /// </summary>
    Task<DividendMutationResult> CreateAsync(CreateDividendRequest request, CancellationToken ct);

    Task<DividendMutationResult> UpdateAsync(Guid transactionId, UpdateDividendRequest request, CancellationToken ct);
}

public record CreateDividendRequest(
    Guid AccountId,
    Guid PaymentCurrencyId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description);

public record UpdateDividendRequest(
    Guid AccountId,
    Guid PaymentCurrencyId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description);

public record DividendMutationResult(
    AccountTransaction? Transaction,
    IReadOnlyCollection<TouchedPair> TouchedPairs);
