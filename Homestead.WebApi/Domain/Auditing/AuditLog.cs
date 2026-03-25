namespace Homestead.WebApi.Domain.Auditing;

public class AuditLog
{
    public Guid Id { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid? ParentEntityId { get; set; }
    public AuditAction Action { get; set; }
    public string? Changes { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
