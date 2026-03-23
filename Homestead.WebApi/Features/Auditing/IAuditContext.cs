using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Features.Auditing;

public interface IAuditContext
{
    void Created(string entityType, Guid entityId, Guid? parentEntityId = null);

    void Track<TSnapshot, TEntity>(TEntity entity, string entityType, Guid entityId,
        Guid? parentEntityId = null)
        where TSnapshot : IAuditSnapshot<TSnapshot, TEntity>;

    void Track(Func<List<FieldChange>> deferredDiff, string entityType, Guid entityId,
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
