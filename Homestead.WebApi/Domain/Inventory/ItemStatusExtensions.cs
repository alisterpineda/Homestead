namespace Homestead.WebApi.Domain.Inventory;

public static class ItemStatusExtensions
{
    /// <summary>
    /// Statuses that keep an item out of the default picture: <see cref="ItemStatus.Sold"/>,
    /// <see cref="ItemStatus.Discarded"/>, <see cref="ItemStatus.Archived"/>.
    /// </summary>
    public static readonly ItemStatus[] TerminalStatuses =
        [ItemStatus.Sold, ItemStatus.Discarded, ItemStatus.Archived];

    /// <summary>
    /// Non-terminal statuses surfaced in default views — used to scope "live" queries.
    /// </summary>
    public static readonly ItemStatus[] LiveStatuses =
        [ItemStatus.Active, ItemStatus.Lent, ItemStatus.InRepair];

    /// <summary>True when the status drops an item out of default lists (Sold/Discarded/Archived).</summary>
    public static bool IsTerminal(this ItemStatus status) =>
        status is ItemStatus.Sold or ItemStatus.Discarded or ItemStatus.Archived;
}
