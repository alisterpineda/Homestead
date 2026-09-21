using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Common.Auditing;

public class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _db;
    private readonly Func<Guid?> _resolveUserId;

    public AuditLogger(AppDbContext db, ICurrentUserAccessor currentUser)
    {
        _db = db;
        _resolveUserId = () => currentUser.UserId;
    }

    private AuditLogger(AppDbContext db, Guid? userId)
    {
        _db = db;
        _resolveUserId = () => userId;
    }

    public IAuditLogger ForUser(Guid? userId) => new AuditLogger(_db, userId);

    public void LogCreated(string entityType, Guid entityId, Guid? parentEntityId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = AuditAction.Created,
            UserId = _resolveUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public void LogUpdated(string entityType, Guid entityId, List<FieldChange> changes,
        Guid? parentEntityId = null)
    {
        if (changes.Count == 0)
            return;

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = AuditAction.Updated,
            Changes = JsonSerializer.Serialize(changes, FieldChange.JsonOptions),
            UserId = _resolveUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public void LogDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        LogAction(AuditAction.Deleted, entityType, entityId, description, parentEntityId);
    }

    public void LogSoftDeleted(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        LogAction(AuditAction.SoftDeleted, entityType, entityId, description, parentEntityId);
    }

    public void LogRestored(string entityType, Guid entityId, string? description = null,
        Guid? parentEntityId = null)
    {
        LogAction(AuditAction.Restored, entityType, entityId, description, parentEntityId);
    }

    private void LogAction(AuditAction action, string entityType, Guid entityId,
        string? description, Guid? parentEntityId)
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

        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            ParentEntityId = parentEntityId,
            Action = action,
            Changes = changes,
            UserId = _resolveUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
