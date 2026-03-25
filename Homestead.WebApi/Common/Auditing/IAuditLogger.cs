using Homestead.WebApi.Domain.Auditing;

namespace Homestead.WebApi.Common.Auditing;

public interface IAuditLogger
{
    void LogCreated(string entityType, Guid entityId, Guid? parentEntityId = null);

    void LogUpdated(string entityType, Guid entityId, List<FieldChange> changes,
        Guid? parentEntityId = null);

    void LogDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    void LogSoftDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    void LogRestored(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null);

    IAuditLogger ForUser(Guid? userId);
}
