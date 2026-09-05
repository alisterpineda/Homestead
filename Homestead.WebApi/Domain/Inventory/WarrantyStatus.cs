namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// An item's warranty cover at a point in time — derived from its <see cref="ItemWarranty"/> rows by
/// <see cref="WarrantyStatusRules"/> and <b>never stored</b>. Serialized as a string by the global
/// <c>JsonStringEnumConverter</c>.
/// </summary>
public enum WarrantyStatus
{
    /// <summary>No warranty rows at all.</summary>
    None,

    /// <summary>Covered — an open-ended row, or a latest expiry beyond the expiring window.</summary>
    Active,

    /// <summary>The latest expiry falls inside <see cref="WarrantyStatusRules.ExpiringWindowDays"/> days.</summary>
    Expiring,

    /// <summary>Every row has lapsed — the latest expiry is in the past.</summary>
    Expired,
}
