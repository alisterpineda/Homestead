namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// One remembered <c>(target, path) → folder id</c> mapping for an id-addressed destination.
/// </summary>
/// <remarks>
/// <para>
/// Exists because Drive folders are objects with ids, sibling folders may legally share a name, and name
/// lookup is eventually consistent — so concurrent find-or-create is a race that mints twin year-folders
/// and splits the mirror permanently. Folders are therefore resolved <i>once</i>, by a sequential ensure
/// step before batch fan-out, and remembered here; batches only ever read ids out of this map and never
/// name-resolve. Steady state costs zero folder calls.
/// </para>
/// <para>
/// Keyed on the target rather than the pairing: the folder tree belongs to the destination, and two
/// pairings mirroring different kinds to the same Drive share its root. Cascades from
/// <c>backup.target_state</c> — a disconnect that clears the grant also invalidates every id in it,
/// because ids are meaningless in another account's Drive.
/// </para>
/// </remarks>
public class BackupTargetFolder
{
    public Guid Id { get; set; }

    /// <summary>The <c>Backup:Targets:{key}</c> this folder lives under. Cascades — see the remarks.</summary>
    public required string TargetKey { get; set; }

    /// <summary>
    /// Forward-slash path from the target root, including the kind root folder (e.g. <c>Documents/2024</c>).
    /// The empty string is the target root folder itself, which is a map entry like any other.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>The destination's own id for the folder.</summary>
    public required string RemoteFolderId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
