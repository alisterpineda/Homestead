namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingEntityType
{
    Document,
    Download,

    // A configured backup target, addressed by DeterministicGuid.FromKey(targetKey) — the target's
    // identity is a config string, not a Guid, and EntityId is a Guid column.
    BackupTarget
}
