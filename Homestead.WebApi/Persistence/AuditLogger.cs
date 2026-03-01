using System.Text.Json;
using Homestead.WebApi.Domains.Auditing;

namespace Homestead.WebApi.Persistence;

public static class AuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void LogCreated(AppDbContext db, string entityType, Guid entityId, Guid? userId,
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

    public static void LogUpdated(AppDbContext db, string entityType, Guid entityId, Guid? userId,
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
            Changes = JsonSerializer.Serialize(changes, JsonOptions),
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public static void LogDeleted(AppDbContext db, string entityType, Guid entityId, Guid? userId,
        string? description = null, Guid? parentEntityId = null)
    {
        string? changes = null;
        if (description is not null)
        {
            var fieldChanges = new List<FieldChange>
            {
                new ScalarFieldChange("Description", description, null)
            };
            changes = JsonSerializer.Serialize(fieldChanges, JsonOptions);
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
