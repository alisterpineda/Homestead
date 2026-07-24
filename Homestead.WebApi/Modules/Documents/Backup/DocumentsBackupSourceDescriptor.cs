using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Backup;

namespace Homestead.WebApi.Modules.Documents.Backup;

/// <summary>
/// Registers the document library as a backup-able source kind. Lives in the Documents module, not in
/// <c>Infrastructure/Backup</c>: <c>AddBackup</c> must stay ignorant of which modules offer backups.
/// </summary>
public sealed class DocumentsBackupSourceDescriptor : IBackupSourceDescriptor
{
    /// <summary>Persisted in <c>backup.sources.source_kind</c>. Changing it strands existing rows.</summary>
    public const string SourceKey = "documents";

    /// <summary>The folder every documents mirror lives under, relative to the target root.</summary>
    public const string RootFolderName = "Documents";

    public string Key => SourceKey;

    public string DisplayName => "Documents";

    // NOT renamed to something kind-agnostic: the job type is a persisted enum on processing.jobs and
    // processing.schedules, and per-kind planner job types are the design — a Finances export would add
    // its own rather than share this one.
    public ProcessingJobType JobType => ProcessingJobType.BackupToTarget;

    public string RootFolder => RootFolderName;

    public bool SupportsPrune => true;
}
