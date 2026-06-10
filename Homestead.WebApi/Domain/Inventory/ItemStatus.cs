namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// The real-world state of the physical thing an <see cref="Item"/> represents — domain lifecycle,
/// not soft-delete. Fungible supplies use only <see cref="Active"/> / <see cref="Archived"/>.
/// </summary>
public enum ItemStatus
{
    /// <summary>Owned and in use or storage. The default.</summary>
    Active,

    /// <summary>Lent out, with a free-text <c>LentTo</c> note.</summary>
    Lent,

    /// <summary>Temporarily out for service.</summary>
    InRepair,

    /// <summary>Sold off. Terminal.</summary>
    Sold,

    /// <summary>Thrown out. Terminal.</summary>
    Discarded,

    /// <summary>Kept in records but not part of the active picture. Terminal.</summary>
    Archived,
}
