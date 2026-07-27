namespace Homestead.WebApi.Infrastructure.Backup;

// One options bag per configured target instance, discriminated by Kind — the config binder needs a
// single concrete type to bind each "Backup:Targets:{key}" section onto (same shape as AiProviderOptions
// and OcrEngineOptions). Per-kind required fields are validated in BackupConfiguration rather than split
// into subclasses.
public class BackupTargetOptions
{
    public const string SectionName = "Backup:Targets";

    // Nullable so "not configured" is distinguishable from Local, and — like the AI bag — required for
    // EVERY key: there is no built-in target whose kind its name could imply. A non-nullable enum would
    // bind an omitted Kind to default(0) = Local, so an operator who configures a future cloud target but
    // forgets Kind would get a target that quietly writes to a local directory under the name they chose.
    public BackupTargetKind? Kind { get; set; }

    // Local: the mirror root directory. Created on demand at first write.
    public string? Path { get; set; }

    // --- GoogleDrive ---
    //
    // The admin brings their own Google Cloud project and OAuth client: a self-hosted open-source app has
    // no server-side secret to hide and no single project Google could verify. Only the client lives in
    // config; the grant it mints is database state.

    /// <summary>The OAuth client id, type "TVs &amp; Limited Input devices".</summary>
    public string? ClientId { get; set; }

    /// <summary>The OAuth client secret. Google shows it exactly once, at client creation.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The app-managed folder in My Drive that the mirror lives under. Defaults to "Homestead Backup" at
    /// use rather than here, so an explicitly blank value in config still falls back rather than
    /// producing a nameless folder.
    /// </summary>
    public string? RootFolderName { get; set; }

    // --- Per-target batch-cap overrides (any kind) ---
    //
    // The engine's caps are per-KIND-of-source ("Backup:Documents:*") and were sized against local disk.
    // Against a residential uplink the same numbers are wildly wrong, and the right ones depend on the
    // destination rather than on what is being mirrored — so they override per target. Null = use the
    // kind's effective default. See BackupBatchCaps.

    public int? MaxFilesPerBatch { get; set; }
    public long? MaxBytesPerBatch { get; set; }
    public int? MaxOpsPerMetadataBatch { get; set; }
}
