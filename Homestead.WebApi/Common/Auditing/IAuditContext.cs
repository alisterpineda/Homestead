using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Common.Auditing;

public interface IAuditContext
{
    void Created(string entityType, Guid entityId, Guid? parentEntityId = null);

    void Track<TSnapshot, TEntity>(TEntity entity, string entityType, Guid entityId,
        Guid? parentEntityId = null)
        where TSnapshot : IAuditSnapshot<TSnapshot, TEntity>;

    void Track(Func<List<FieldChange>> deferredDiff, string entityType, Guid entityId,
        Guid? parentEntityId = null);

    /// <summary>
    /// Convenience for a single fixed scalar field change (the value is already known, so no deferral is
    /// needed). Equivalent to <see cref="Track(Func{List{FieldChange}}, string, Guid, Guid?)"/> with a
    /// one-element <see cref="ScalarFieldChange"/> list.
    /// </summary>
    void TrackScalar(string entityType, Guid entityId, string field, string? oldValue, string? newValue,
        Guid? parentEntityId = null);

    void Deleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    void SoftDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    void Restored(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    bool HasEntries { get; }

    void FlushTo(IAuditLogger auditLogger);
}
