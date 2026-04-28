using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface IBalanceEntryService
{
    /// <summary>
    /// Records a balance assertion: the account's balance in a given commodity on a given date.
    /// The server computes <c>delta = signedAsserted − prior running</c> from current DB state and
    /// books it against <c>Equity:Adjustments</c>.
    ///
    /// Does NOT call SaveChangesAsync. Returns the entry plus touched pairs the caller must pass
    /// to <see cref="IBalanceEntryReconciler"/> after the primary change has been flushed (the
    /// reconciler handles re-derivation of any downstream balance entries).
    /// </summary>
    Task<BalanceEntryMutationResult> CreateAsync(CreateBalanceEntryRequest request, CancellationToken ct);

    /// <summary>
    /// Updates an existing balance entry. <paramref name="expectedAccountId"/> scopes the URL
    /// (the handler's <c>/accounts/{accountId}/balance-entries/{id}</c> route); the service
    /// rejects the update when the persisted entry belongs to a different account.
    /// </summary>
    Task<BalanceEntryMutationResult> UpdateAsync(Guid transactionId, Guid expectedAccountId,
        UpdateBalanceEntryRequest request, CancellationToken ct);

    Task<BalanceEntryMutationResult> DeleteAsync(Guid transactionId, CancellationToken ct);
}

public record CreateBalanceEntryRequest(
    Guid AccountId,
    Guid CommodityId,
    decimal AssertedAmount,
    DateOnly Date,
    string? Description);

public record UpdateBalanceEntryRequest(
    Guid CommodityId,
    decimal AssertedAmount,
    DateOnly Date,
    string? Description);

public record BalanceEntryMutationResult(
    BalanceEntryTransaction? Entry,
    IReadOnlyCollection<TouchedPair> TouchedPairs);
