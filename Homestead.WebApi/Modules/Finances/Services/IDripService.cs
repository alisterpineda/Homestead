using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IDripService
{
    /// <summary>
    /// Creates a DRIP (dividend reinvestment) as two linked <see cref="NormalTransaction"/>s at one
    /// instant: a Dividend half (cash into the investment account, counterpart = system Income) and an
    /// Investment-Buy half (that same cash reinvested into security units). The two are paired via
    /// <see cref="AccountTransaction.RelatedTransactionId"/> in both directions. Only the Buy half is
    /// annotated, so it materialises exactly one linked CommodityPrice row at
    /// (security, paymentCurrency, OccurredAt) with the derived per-unit price.
    ///
    /// Does NOT call SaveChangesAsync. Returns both halves plus the union of (account, commodity,
    /// earliestAffectedAt) triples the caller must pass to <see cref="IBalanceEntryReconciler"/>
    /// after the primary change has been flushed.
    /// </summary>
    Task<DripMutationResult> CreateAsync(CreateDripRequest request, CancellationToken ct);

    /// <summary>
    /// Atomically edits both halves of a DRIP pair (amount, units, date/time, description). The
    /// account, security, and payment currency are immutable (change them via delete + recreate).
    /// </summary>
    Task<DripMutationResult> UpdateAsync(Guid transactionId, UpdateDripRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes BOTH halves of a DRIP pair atomically (the "Delete both" path). Deleting a single half
    /// standalone is the generic <c>DeleteTransaction</c> path instead, which leaves the survivor as a
    /// standalone Dividend / Buy via the DB <c>OnDelete(SetNull)</c>.
    /// </summary>
    Task<DripMutationResult> DeleteBothAsync(Guid transactionId, CancellationToken ct);
}

// Single payment currency in v1 — it is BOTH the dividend currency and the reinvestment funding
// currency. (Cross-currency DRIP — dividend in USD, account reports CAD — is a documented v2 item.)
public record CreateDripRequest(
    Guid AccountId,
    Guid SecurityId,
    Guid PaymentCurrencyId,
    decimal Amount,
    decimal Units,
    DateTimeOffset OccurredAt,
    string? Description);

public record UpdateDripRequest(
    Guid AccountId,
    Guid SecurityId,
    Guid PaymentCurrencyId,
    decimal Amount,
    decimal Units,
    DateTimeOffset OccurredAt,
    string? Description);

// Carries BOTH halves (unlike the single-transaction *MutationResult records) so the handler can
// return either and aggregate touched pairs across both.
public record DripMutationResult(
    AccountTransaction? DividendHalf,
    AccountTransaction? BuyHalf,
    IReadOnlyCollection<TouchedPair> TouchedPairs);
