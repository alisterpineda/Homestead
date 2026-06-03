using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Small pure helpers shared by the per-shape transaction services. Deliberately limited to
/// shape-agnostic utilities — the services themselves stay separate per the per-shape-editor
/// design (consolidation of shape behaviour is explicitly deferred).
/// </summary>
internal static class TransactionWriteHelpers
{
    /// <summary>Trim a user-supplied memo; null/blank becomes the empty string.</summary>
    public static string NormalizeMemo(string? memo) => memo?.Trim() ?? "";

    /// <summary>
    /// One <see cref="TouchedPair"/> per split, at the transaction's instant — the set the caller
    /// hands to the balance-entry reconciler after the primary change is flushed.
    /// </summary>
    public static IEnumerable<TouchedPair> TouchedFor(AccountTransaction transaction)
    {
        foreach (var split in transaction.Splits)
            yield return new TouchedPair(split.AccountId, split.CommodityId, transaction.OccurredAt);
    }
}
