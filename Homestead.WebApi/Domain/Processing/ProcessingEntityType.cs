namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingEntityType
{
    Document,
    Download,

    // A backup.sources row — one "mirror <kind> to <target>" pairing. EntityId is the row's own Guid, so
    // unlike the config-keyed target it replaced, no DeterministicGuid derivation is involved. Batch
    // children share their parent's EntityId; a query that means "the run itself" must also filter
    // ParentJobId == null.
    BackupSource
}
