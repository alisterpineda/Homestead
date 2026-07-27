namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// What a destination can do and how long a name it will accept. The path planner reads this so a name
/// is trimmed to fit the destination BEFORE anything is written — a rejected write halfway through a
/// batch is far worse than a shortened name.
/// </summary>
/// <param name="MaxSegmentLength">Longest single path segment (file or folder name) the target accepts.</param>
/// <param name="MaxPathLength">Longest full relative path the target accepts.</param>
/// <param name="SupportsMove">
/// True when the target can rename in place. False would force a re-upload on every rename, so the plan
/// builder degrades a Move into a Put + Delete.
/// </param>
/// <param name="SupportsContentHash">
/// True when the target reports a content hash we can compare against <c>DocumentVersion.FileHash</c>
/// without re-reading the blob. Unused in Phase 1 (the local target has no cheaper answer than reading
/// the file, which is what the state row exists to avoid) — declared so a Phase 2 cloud target can
/// verify a copy landed intact without downloading it back.
/// </param>
/// <param name="DefersFolderPrune">
/// True when emptied folders must be swept by a final pass after every mutation in the run, rather than
/// inline as each delete happens. False for a filesystem, where "is this directory empty right now" is a
/// true answer; true where listing is eventually consistent and an inline check would race the same run's
/// own moves — trashing a folder with a just-arrived file inside it whose state row still reads
/// "mirrored". The planner reads this to decide whether to emit a final prune batch.
/// </param>
public readonly record struct BackupTargetCapabilities(
    int MaxSegmentLength,
    int MaxPathLength,
    bool SupportsMove,
    bool SupportsContentHash,
    bool DefersFolderPrune = false)
{
    /// <summary>
    /// Conservative caps that work everywhere: 255 bytes per segment is the floor across ext4/APFS/NTFS,
    /// and 4096 total is Linux's PATH_MAX. Used for a target that cannot state its own limits.
    /// </summary>
    public static BackupTargetCapabilities Default { get; } = new(
        MaxSegmentLength: 255,
        MaxPathLength: 4096,
        SupportsMove: true,
        SupportsContentHash: false);
}
