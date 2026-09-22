using BookVerse.Domain.Common;

namespace BookVerse.Domain.Entities.Audit;

public class AuditLog : Entity<Guid>
{
    public Guid? UserId { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset Timestamp { get; private set; } = DateTimeOffset.UtcNow;

    private AuditLog() { }

    public static AuditLog Create(
        Guid? userId,
        string entityName,
        string entityId,
        string action,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = ipAddress,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
