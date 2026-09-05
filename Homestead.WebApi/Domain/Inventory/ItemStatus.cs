namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// The real-world state of the physical thing an <see cref="Item"/> represents — domain lifecycle,
/// not soft-delete. Fungible consumables use only <see cref="Active"/> / <see cref="Archived"/>,
/// so the kinds differ only by <see cref="Lent"/>.
/// </summary>
public enum ItemStatus
{
    /// <summary>Owned and in use or storage. The default. Carries no <c>StatusNote</c>.</summary>
    Active,

    /// <summary>Lent out; <c>StatusNote</c> carries to whom (required). Non-fungible only.</summary>
    Lent,

    /// <summary>
    /// The single terminal state — kept in records but out of the active picture (sold, thrown out,
    /// or simply retired). <c>StatusNote</c> optionally carries why ("Sold to Sam").
    /// </summary>
    Archived,
}
