namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// Discriminates the two kinds of <see cref="Item"/>. Immutable for the life of a row — converting
/// between kinds is done by creating/retiring rows, never by toggling this.
/// </summary>
public enum Fungibility
{
    /// <summary>A single, identity-tracked instance that lives in one place (or nowhere yet).</summary>
    NonFungible,

    /// <summary>A type of consumable measured in a unit, whose quantity spreads across stock entries.</summary>
    Fungible,
}
