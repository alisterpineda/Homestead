using Homestead.WebApi.Domains.Auditing;

namespace Homestead.WebApi.Persistence;

public interface IAuditLogger
{
    void LogCreated(string entityType, Guid entityId, Guid? userId, Guid? parentEntityId = null);

    void LogUpdated(string entityType, Guid entityId, Guid? userId, List<FieldChange> changes,
        Guid? parentEntityId = null);

    void LogDeleted(string entityType, Guid entityId, Guid? userId, string? description = null,
        Guid? parentEntityId = null);
}
