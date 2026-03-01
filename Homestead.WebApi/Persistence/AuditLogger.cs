using System.Text.Json;
using Homestead.WebApi.Domains.Auditing;

namespace Homestead.WebApi.Persistence;

public class AuditLogger(AppDbContext db) : IAuditLogger
{

    public void LogCreated(string entityType, Guid entityId, Guid? userId,
        Guid? parentEntityId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = AuditAction.Created,
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public void LogUpdated(string entityType, Guid entityId, Guid? userId,
        List<FieldChange> changes, Guid? parentEntityId = null)
    {
        if (changes.Count == 0)
            return;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = AuditAction.Updated,
            Changes = JsonSerializer.Serialize(changes, FieldChange.JsonOptions),
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public void LogDeleted(string entityType, Guid entityId, Guid? userId,
        string? description = null, Guid? parentEntityId = null)
    {
        string? changes = null;
        if (description is not null)
        {
            var fieldChanges = new List<FieldChange>
            {
                new ScalarFieldChange("Description", description, null)
            };
            changes = JsonSerializer.Serialize(fieldChanges, FieldChange.JsonOptions);
        }

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = AuditAction.Deleted,
            Changes = changes,
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
