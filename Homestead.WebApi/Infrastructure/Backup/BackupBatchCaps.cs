namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The effective batching budget for one run — how much work the planner packs into a single child job.
/// </summary>
/// <remarks>
/// Two independent things set these numbers, which is why they are resolved rather than read. The
/// <i>source kind</i> knows how expensive its own units of work are (a document copy moves bytes; a
/// generated-export rewrite might not), and the <i>destination</i> knows how fast they can possibly go.
/// Phase 1 had only the first, sized against local disk. A residential uplink makes the destination the
/// binding constraint — 512 MB in one batch needs ~14 Mbps sustained to fit the processor's five-minute
/// timeout — so the target's own numbers win where it states them.
/// </remarks>
public readonly record struct BackupBatchCaps(
    int MaxFilesPerBatch,
    long MaxBytesPerBatch,
    int MaxOpsPerMetadataBatch)
{
    /// <summary>
    /// Combines a source kind's caps with a target's overrides, applying floors to both.
    /// </summary>
    /// <remarks>
    /// The floors are not defensive decoration: a degenerate configured value (0, negative) would produce
    /// zero-capacity batches and an infinite packing loop, so every cap is clamped on the way through
    /// rather than trusted from config.
    /// </remarks>
    public static BackupBatchCaps Resolve(
        int kindMaxFilesPerBatch,
        long kindMaxBytesPerBatch,
        int kindMaxOpsPerMetadataBatch,
        BackupBatchCapOverrides overrides) => new(
        MaxFilesPerBatch: Math.Max(1, overrides.MaxFilesPerBatch ?? kindMaxFilesPerBatch),
        MaxBytesPerBatch: Math.Max(1, overrides.MaxBytesPerBatch ?? kindMaxBytesPerBatch),
        MaxOpsPerMetadataBatch: Math.Max(1, overrides.MaxOpsPerMetadataBatch ?? kindMaxOpsPerMetadataBatch));
}

/// <summary>
/// A target's say in how work is batched for it. Every member null means "no opinion — use the source
/// kind's caps", which is the local target's answer unless an operator configured otherwise.
/// </summary>
public readonly record struct BackupBatchCapOverrides(
    int? MaxFilesPerBatch,
    long? MaxBytesPerBatch,
    int? MaxOpsPerMetadataBatch);
