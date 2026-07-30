namespace Homestead.WebApi.Domain.Backup;

/// <summary>
/// Mutable operator state for one configured backup target. One row per <c>Backup:Targets:{key}</c>, but
/// created lazily — a target that has never been touched has no row and reads as enabled.
/// </summary>
/// <remarks>
/// Deliberately holds only what config cannot, and only what is true of the <i>destination</i> rather
/// than of any one dataset mirrored to it: whether the operator turned the whole target off. The delete
/// breaker latch lives on <see cref="BackupSource"/>, because a latch is a fact about one pairing's
/// change set. Last-run status is on neither — it is derived from <c>processing.jobs</c>, because a
/// parent job's terminal transition happens inside the generic
/// <c>JobProcessorService.UpdateParentProgressAsync</c>, which offers no per-type hook, so a
/// handler-written status column could never be set reliably.
/// </remarks>
public class BackupTargetState
{
    /// <summary>The config key. Natural primary key — targets are named by config, not by surrogate id.</summary>
    public required string TargetKey { get; set; }

    /// <summary>
    /// False suspends scheduled and manual runs for <i>every</i> source paired with this target, without
    /// deleting the config or any state rows.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // --- Credential lifecycle (GoogleDrive kinds; all null for Local) ---
    //
    // A Drive target can be *connected* as well as configured, which is the one thing Phase 2 added that
    // config genuinely cannot express: the grant is minted by a human consenting at google.com/device,
    // so it has nowhere to live but a row. All five columns are destination-level, not pairing-level —
    // one revoked grant stops every kind mirrored to this target at once.

    /// <summary>
    /// Data-Protection ciphertext of the OAuth grant JSON (refresh token + last access token). Encrypted
    /// rather than plain so a casual read of the table — a support dump, a partial disclosure — does not
    /// hand over a live credential. See the design doc's honest caveat: the key ring lives in this same
    /// database, so this is not protection against a full compromise.
    /// </summary>
    public string? EncryptedGrant { get; set; }

    /// <summary>
    /// The connected Google account, plain so the card can read "Connected as …" without decrypting
    /// anything — and so a reconnect can detect that a <i>different</i> account is being attached.
    /// </summary>
    public string? ConnectedAccountEmail { get; set; }

    /// <summary>
    /// When the current grant was issued. Feeds the seven-day heuristic: an authorization that dies about
    /// a week after issue is almost certainly an OAuth app left in Testing status.
    /// </summary>
    public DateTimeOffset? GrantIssuedAt { get; set; }

    /// <summary>
    /// When the grant was first observed to be dead. The persisted half of lazy detection: once set,
    /// queued sibling batches short-circuit off this row instead of hammering Google's token endpoint in
    /// turn. Cleared by a successful reconnect or validate.
    /// </summary>
    public DateTimeOffset? AuthFailedAt { get; set; }

    /// <summary>
    /// The reason, pre-built at latch time by the token manager rather than derived at read time — the
    /// seven-day heuristic needs <see cref="GrantIssuedAt"/> and the failure moment together, and a
    /// decrypt failure is a different sentence from a Google-side revocation.
    /// </summary>
    public string? AuthFailedReason { get; set; }

    // --- The resolved root (GoogleDrive kinds; all null for Local) ---

    /// <summary>The root folder path as actually created, forward-slash. Null until one exists.</summary>
    /// <remarks>
    /// <para>
    /// The <i>resolved</i> root, not the configured one: config supplies the default on first creation and
    /// this records what came of it, so the admin card displays a location that is true by construction
    /// rather than one config merely asks for.
    /// </para>
    /// <para>
    /// <b>It pins the mirror.</b> <see cref="DocumentBackupState"/> rows hold root-relative paths plus
    /// Drive file ids, so honouring a config edit after creation would mint a new empty root while every
    /// copied file stayed physically in the old tree — a mirror reporting converged and silently wrong.
    /// It therefore survives a disconnect (which keeps those copy records) and is cleared only by a
    /// confirmed account switch, which deletes them.
    /// </para>
    /// <para>
    /// The folder's <b>id</b> lives in <c>backup.target_folders</c> under path key <c>""</c> and is not
    /// duplicated here: the two rows have different clearing rules, so a copy would be cleared in one
    /// place and not the other on the most common path — and what it would buy is one <c>files.list</c>
    /// per segment, once, on a reconnect that oldest-wins find already lands on the same folder for.
    /// </para>
    /// </remarks>
    public string? RootFolderPath { get; set; }

    /// <summary>Drive's own URL for that folder, so the card can link straight to it.</summary>
    /// <remarks>
    /// The only workable mitigation for the root's collidable top segment: the <c>drive.file</c> scope
    /// cannot see a folder the app did not create, so a same-named folder of the user's own can never be
    /// detected, let alone warned about. One click answers "which of these is the app's". Kept across a
    /// disconnect on purpose — the disconnect dialog already says the files stay in that account's Drive,
    /// and a live link makes that advice actionable.
    /// </remarks>
    public string? RootFolderWebViewLink { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
