namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The "Backup" config section: the set of named targets plus the knobs that shape how the planner
/// batches work and when it refuses to prune.
/// </summary>
public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>One entry per "Backup:Targets:{key}" child. Empty = backup off, the graceful no-op.</summary>
    public Dictionary<string, BackupTargetOptions> Targets { get; set; } = [];

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
    /// Fraction of the target's already-backed-up file count above which deletes trip the breaker. Scales
    /// the guard with the library so a big mirror isn't pruned wholesale by one bad run.
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

    // Floors: a degenerate configured value (0, negative) would produce zero-capacity batches and an
    // infinite planning loop, so each cap is read through a resolver rather than used raw.
    public int ResolveMaxFilesPerBatch() => Math.Max(1, MaxFilesPerBatch);
    public long ResolveMaxBytesPerBatch() => Math.Max(1, MaxBytesPerBatch);
    public int ResolveMaxOpsPerMetadataBatch() => Math.Max(1, MaxOpsPerMetadataBatch);
    public int ResolveMinAbsoluteDeletes() => Math.Max(0, MinAbsoluteDeletes);
    public double ResolveDeleteFraction() => Math.Clamp(DeleteFraction, 0d, 1d);
}
