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
    /// The empty string is the <i>effective</i> target root — for Drive, the id of the last segment of the
    /// root path (<c>Homestead/Backup</c>'s <c>Backup</c>).
    /// </summary>
    /// <remarks>
    /// The chain above that root is deliberately <b>unmapped</b>: keys stay strictly root-relative, which
    /// is what keeps <c>PruneUpwardsAsync</c>'s "stop at the last slash" guard a <i>structural</i>
    /// guarantee that nothing above the effective root can be trashed. Admitting above-the-root entries
    /// would downgrade it to a rule someone has to remember, and the failure mode is trashing the user's
    /// own top-level <c>Homestead</c>. The intermediates are re-derived by name lookup whenever <c>""</c>
    /// is unmapped, which costs one <c>files.list</c> per segment, once per process.
    /// </remarks>
    public required string Path { get; set; }

    /// <summary>The destination's own id for the folder.</summary>
    public required string RemoteFolderId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
