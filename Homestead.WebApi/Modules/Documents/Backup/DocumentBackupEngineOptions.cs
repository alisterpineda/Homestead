namespace Homestead.WebApi.Modules.Documents.Backup;

/// <summary>
/// The "Backup:Documents" config section: how the documents planner batches work and when it refuses to
/// prune. Per-kind rather than shared, because the right numbers depend on what is being mirrored — a
/// generated-export kind that replaces files wholesale would count far more deletes for the same amount
/// of change, and needs its own thresholds.
/// </summary>
public class DocumentBackupEngineOptions
{
    public const string SectionName = "Backup:Documents";

    // --- Circuit breaker ---
    //
    // The mirror deletes, which means a bug (or a bad restore of the Postgres volume against an intact
    // mirror) could wipe the only offline copy of the library. The breaker turns "delete a lot" into an
    // admin decision: below BOTH thresholds deletes flow, above EITHER they are latched and skipped while
    // puts and moves still run.

    /// <summary>
    /// Deletes at or below this count never trip the breaker, however large a fraction of the mirror they
    /// are. Small libraries would otherwise trip on routine cleanup — 3 deletes out of 20 files is 15%.
    /// </summary>
    public int MinAbsoluteDeletes { get; set; } = 25;

    /// <summary>
    /// Fraction of the pairing's already-backed-up file count above which deletes trip the breaker.
    /// Scales the guard with the library so a big mirror isn't pruned wholesale by one bad run.
    /// </summary>
    public double DeleteFraction { get; set; } = 0.10;

    // --- Batching ---
    //
    // A single "back up everything" job would blow the processor's hard-coded 5-minute per-job timeout on
    // first run and starve the queue behind it (MaxConcurrency defaults to 1). The planner therefore fans
    // out fixed-size children; these are the caps.

    /// <summary>Max file copies per child batch.</summary>
    public int MaxFilesPerBatch { get; set; } = 100;

    /// <summary>
    /// Max total bytes per child batch. Binds before the file cap for large scans/videos, keeping one
    /// batch comfortably inside the 5-minute timeout even on slow storage.
    /// </summary>
    public long MaxBytesPerBatch { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Max metadata-only operations (moves + deletes) per child batch. Higher than the file cap because
    /// these are constant-time per op — no bytes move.
    /// </summary>
    public int MaxOpsPerMetadataBatch { get; set; } = 500;

    // Floors for the breaker thresholds. The three batching caps above are floored by BackupBatchCaps
    // instead, which is also where a per-target override gets to win — the destination knows how fast a
    // batch can possibly go, and these numbers were sized against local disk.
    public int ResolveMinAbsoluteDeletes() => Math.Max(0, MinAbsoluteDeletes);
    public double ResolveDeleteFraction() => Math.Clamp(DeleteFraction, 0d, 1d);
}
