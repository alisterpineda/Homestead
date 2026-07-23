namespace Homestead.WebApi.Infrastructure.Backup;

// Lives here rather than Domain (contrast TextExtractionEngineType): the target kind is config-only,
// never persisted — it only ever serializes into the admin backup-targets response. State rows key off
// the target KEY, so adding a kind never touches the schema.
public enum BackupTargetKind
{
    // A directory on a filesystem the WebApi process can write to — a local disk, a mounted NAS share,
    // a bind-mounted volume. Path is the mirror root. The only kind in Phase 1.
    Local
}
