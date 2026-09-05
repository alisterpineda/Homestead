namespace Homestead.WebApi.Domain.Inventory;

public static class ItemStatusExtensions
{
    /// <summary>
    /// Statuses that keep an item out of the default picture — just <see cref="ItemStatus.Archived"/>,
    /// the single terminal state.
    /// </summary>
    public static readonly ItemStatus[] TerminalStatuses = [ItemStatus.Archived];

    /// <summary>
    /// Non-terminal statuses surfaced in default views — used to scope "live" queries.
    /// </summary>
    public static readonly ItemStatus[] LiveStatuses = [ItemStatus.Active, ItemStatus.Lent];

    /// <summary>True when the status drops an item out of default lists (Archived).</summary>
    public static bool IsTerminal(this ItemStatus status) => status is ItemStatus.Archived;
}
