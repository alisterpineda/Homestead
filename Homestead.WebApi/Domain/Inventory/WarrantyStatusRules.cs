namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// The shared constants and the row-level rule for how warranties resolve to a
/// <see cref="WarrantyStatus"/> (warranties.md §Derived status). Pure and storage-free — nothing
/// here is persisted.
/// </summary>
/// <remarks>
/// The <b>item-level</b> rule (an open-ended row wins outright; otherwise the latest expiry decides)
/// lives only as SQL, in two places that must stay in sync with each other and with
/// <see cref="ForRow"/>: the status ternary in <c>DurableResponseMapper.For</c> and the Expiring
/// predicate in <c>GetInventoryOverviewHandler</c> (same posture as the duplicated low-stock
/// predicate). <c>GetDurablesHandlerTests</c> and <c>GetInventoryOverviewHandlerTests</c> pin both.
/// </remarks>
public static class WarrantyStatusRules
{
    /// <summary>
    /// How far ahead an expiry counts as "expiring soon". One constant, used by the item status, the
    /// list facet, and the Browse tile alike.
    /// </summary>
    public const int ExpiringWindowDays = 30;

    /// <summary>Today on the server's UTC clock — the reference date every rule below compares against.</summary>
    public static DateOnly TodayUtc(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>The last date that still counts as "expiring soon" (inclusive).</summary>
    public static DateOnly Cutoff(DateOnly today) => today.AddDays(ExpiringWindowDays);

    /// <summary>
    /// Row-level status for a single warranty. Never <see cref="WarrantyStatus.None"/> — a row that
    /// exists is either open-ended (Active), lapsed (Expired), or inside/outside the window.
    /// </summary>
    public static WarrantyStatus ForRow(DateOnly? expiresOn, DateOnly today)
    {
        if (expiresOn is null)
            return WarrantyStatus.Active;

        if (expiresOn.Value < today)
            return WarrantyStatus.Expired;

        return expiresOn.Value <= Cutoff(today) ? WarrantyStatus.Expiring : WarrantyStatus.Active;
    }
}
