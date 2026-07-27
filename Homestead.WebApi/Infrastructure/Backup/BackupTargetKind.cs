namespace Homestead.WebApi.Infrastructure.Backup;

// Lives here rather than Domain (contrast TextExtractionEngineType): the target kind is config-only,
// never persisted — it only ever serializes into the admin backup-targets response. State rows key off
// the target KEY, so adding a kind never touches the schema.
public enum BackupTargetKind
{
    // A directory on a filesystem the WebApi process can write to — a local disk, a mounted NAS share,
    // a bind-mounted volume. Path is the mirror root. The only kind in Phase 1.
    Local,

    // A personal (non-Workspace) Google account's My Drive, reached through user OAuth with the
    // drive.file scope. Config carries only the admin's own OAuth client (ClientId/ClientSecret) and the
    // root folder name — the grant itself is minted by a human consenting at google.com/device and lives
    // on backup.target_state, because a credential is not a config value.
    GoogleDrive
}
