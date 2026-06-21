namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// The holder-role of a non-fungible <see cref="Item"/> (null for fungible items). Names what a
/// parent <em>is</em>, so a child's relationship is derived from its parent's kind rather than a
/// per-child flag: a child of a <see cref="Container"/> is stored in it; a child of an
/// <see cref="Assembly"/> is a component of it.
/// </summary>
public enum DurableKind
{
    /// <summary>Holds nothing — the default. Cannot be a parent.</summary>
    Plain,

    /// <summary>A storage place. Holds stored-in children and loose stock; appears in Browse.</summary>
    Container,

    /// <summary>A machine that holds components. Not a place: absent from Browse and stock placement.</summary>
    Assembly,
}
