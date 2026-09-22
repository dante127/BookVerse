using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;

namespace BookVerse.Domain.Entities.Library;

public class UserBook : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public UserBookStatus Status { get; private set; } = UserBookStatus.WantToRead;
    public DateTimeOffset AddedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? LastReadAt { get; private set; }

    public User User { get; private set; } = null!;
    public Book Book { get; private set; } = null!;

    private UserBook() { }

    public static UserBook Create(Guid userId, Guid bookId, UserBookStatus status = UserBookStatus.WantToRead)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new UserBook
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = bookId,
            Status = status,
            AddedAt = now
        };

        if (status == UserBookStatus.Reading)
        {
            entry.StartedAt = now;
            entry.LastReadAt = now;
        }
        else if (status == UserBookStatus.Completed)
        {
            entry.StartedAt = now;
            entry.CompletedAt = now;
            entry.LastReadAt = now;
        }

        return entry;
    }

    public void TransitionStatus(UserBookStatus newStatus)
    {
        if (Status == newStatus) return;

        var now = DateTimeOffset.UtcNow;
        Status = newStatus;

        switch (newStatus)
        {
            case UserBookStatus.Reading:
                StartedAt ??= now;
                LastReadAt = now;
                break;
            case UserBookStatus.Completed:
                StartedAt ??= now;
                CompletedAt = now;
                LastReadAt = now;
                break;
            case UserBookStatus.Paused:
            case UserBookStatus.Dropped:
                LastReadAt = now;
                break;
        }
    }

    public void RecordReadingActivity()
    {
        LastReadAt = DateTimeOffset.UtcNow;
    }
}

public class FavoriteBook
{
    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public User User { get; private set; } = null!;
    public Book Book { get; private set; } = null!;

    private FavoriteBook() { }

    public FavoriteBook(Guid userId, Guid bookId)
    {
        UserId = userId;
        BookId = bookId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
