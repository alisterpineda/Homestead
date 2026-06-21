namespace Homestead.WebApi.Domain.Inventory;

public static class DurableKindExtensions
{
    /// <summary>
    /// A holder can be a parent: a <see cref="DurableKind.Container"/> (storage) or an
    /// <see cref="DurableKind.Assembly"/> (composition). A <see cref="DurableKind.Plain"/> item — or a
    /// fungible item (null kind) — holds nothing. Single source of truth for the parent-typing gate; EF
    /// <c>Where</c> predicates spell the same test inline because a method call can't be translated.
    /// </summary>
    public static bool IsHolder(this DurableKind? kind) =>
        kind is DurableKind.Container or DurableKind.Assembly;
}
