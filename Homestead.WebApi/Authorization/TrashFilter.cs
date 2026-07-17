namespace Homestead.WebApi.Authorization;

/// <summary>
/// Trash scope for document queries sourced from <see cref="IDocumentQueryService"/>.
/// </summary>
public enum TrashFilter
{
    /// <summary>Only non-trashed documents — same semantics as the global query filter.</summary>
    ActiveOnly,

    /// <summary>Only trashed documents — trash views: list, restore, purge.</summary>
    TrashedOnly,

    /// <summary>Active and trashed — for consumers that must tell a trashed document apart
    /// from an inaccessible one.</summary>
    IncludeTrashed,
}
