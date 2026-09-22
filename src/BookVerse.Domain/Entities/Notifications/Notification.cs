using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Enums;

namespace BookVerse.Domain.Entities.Notifications;

public class Notification : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? ReferenceUrl { get; private set; }
    public bool IsRead { get; private set; } = false;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; private set; }

    public User User { get; private set; } = null!;

    private Notification() { }

    public static Notification Create(Guid userId, NotificationType type, string title, string message, string? referenceUrl = null)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title.Trim(),
            Message = message.Trim(),
            ReferenceUrl = referenceUrl?.Trim(),
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkAsRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
    }
}
