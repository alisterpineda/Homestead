using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Common.Auditing;

public class AuditContext : IAuditContext
{
    private List<AuditEntry>? _entries;

    public bool HasEntries => _entries is { Count: > 0 };

    public void Created(string entityType, Guid entityId, Guid? parentEntityId = null)
    {
        (_entries ??= []).Add(new CreatedEntry(entityType, entityId, parentEntityId));
    }

    public void Track<TSnapshot, TEntity>(TEntity entity, string entityType, Guid entityId,
        Guid? parentEntityId = null)
        where TSnapshot : IAuditSnapshot<TSnapshot, TEntity>
    {
        var before = TSnapshot.From(entity);
        (_entries ??= []).Add(new TrackedUpdateEntry(entityType, entityId, parentEntityId,
            () => before.Diff(TSnapshot.From(entity))));
    }

    public void Track(Func<List<FieldChange>> deferredDiff, string entityType, Guid entityId,
        Guid? parentEntityId = null)
    {
        (_entries ??= []).Add(new TrackedUpdateEntry(entityType, entityId, parentEntityId, deferredDiff));
    }

    public void TrackScalar(string entityType, Guid entityId, string field, string? oldValue,
        string? newValue, Guid? parentEntityId = null)
    {
        var changes = new List<FieldChange> { new ScalarFieldChange(field, oldValue, newValue) };
        (_entries ??= []).Add(new TrackedUpdateEntry(entityType, entityId, parentEntityId, () => changes));
    }

    public void Deleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        (_entries ??= []).Add(new DeletedEntry(entityType, entityId, parentEntityId, description));
    }

    public void SoftDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        (_entries ??= []).Add(new SoftDeletedEntry(entityType, entityId, parentEntityId, description));
    }

    public void Restored(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        (_entries ??= []).Add(new RestoredEntry(entityType, entityId, parentEntityId, description));
    }

    public void FlushTo(IAuditLogger auditLogger)
    {
        if (_entries is null) return;

        foreach (var entry in _entries)
        {
            switch (entry)
            {
                case CreatedEntry e:
                    auditLogger.LogCreated(e.EntityType, e.EntityId, e.ParentEntityId);
                    break;
                case TrackedUpdateEntry e:
                    var changes = e.ComputeDiff();
                    auditLogger.LogUpdated(e.EntityType, e.EntityId, changes, e.ParentEntityId);
                    break;
                case DeletedEntry e:
                    auditLogger.LogDeleted(e.EntityType, e.EntityId, e.Description, e.ParentEntityId);
                    break;
                case SoftDeletedEntry e:
                    auditLogger.LogSoftDeleted(e.EntityType, e.EntityId, e.Description, e.ParentEntityId);
                    break;
                case RestoredEntry e:
                    auditLogger.LogRestored(e.EntityType, e.EntityId, e.Description, e.ParentEntityId);
                    break;
            }
        }

        _entries = null;
    }

    private abstract record AuditEntry(string EntityType, Guid EntityId, Guid? ParentEntityId);

    private sealed record CreatedEntry(string EntityType, Guid EntityId, Guid? ParentEntityId)
        : AuditEntry(EntityType, EntityId, ParentEntityId);

    private sealed record TrackedUpdateEntry(
        string EntityType, Guid EntityId, Guid? ParentEntityId, Func<List<FieldChange>> ComputeDiff)
        : AuditEntry(EntityType, EntityId, ParentEntityId);

    private sealed record DeletedEntry(
        string EntityType, Guid EntityId, Guid? ParentEntityId, string? Description)
        : AuditEntry(EntityType, EntityId, ParentEntityId);

    private sealed record SoftDeletedEntry(
        string EntityType, Guid EntityId, Guid? ParentEntityId, string? Description)
        : AuditEntry(EntityType, EntityId, ParentEntityId);

    private sealed record RestoredEntry(
        string EntityType, Guid EntityId, Guid? ParentEntityId, string? Description)
        : AuditEntry(EntityType, EntityId, ParentEntityId);
}
