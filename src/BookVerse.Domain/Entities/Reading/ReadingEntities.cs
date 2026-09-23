using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;

namespace BookVerse.Domain.Entities.Reading;

public class ReadingProgress : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public int CurrentPage { get; private set; }
    public int TotalPages { get; private set; }
    public decimal Percentage { get; private set; }
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastReadAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public User User { get; private set; } = null!;
    public Book Book { get; private set; } = null!;

    private ReadingProgress() { }

    public static ReadingProgress Create(Guid userId, Guid bookId, int totalPages, int initialPage = 0)
    {
        if (totalPages <= 0)
            throw new ReadingDomainException("Total pages must be greater than zero.");
        if (initialPage < 0 || initialPage > totalPages)
            throw new ReadingDomainException($"Current page must be between 0 and {totalPages}.");

        var now = DateTimeOffset.UtcNow;
        var percentage = Math.Round(((decimal)initialPage / totalPages) * 100, 2);

        var progress = new ReadingProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = bookId,
            CurrentPage = initialPage,
            TotalPages = totalPages,
            Percentage = percentage,
            StartedAt = now,
            LastReadAt = now
        };

        if (initialPage == totalPages)
        {
            progress.CompletedAt = now;
            progress.AddDomainEvent(new BookCompletedEvent(userId, bookId, now));
        }

        return progress;
    }

    public void UpdateProgress(int newPage)
    {
        if (newPage < 0 || newPage > TotalPages)
            throw new ReadingDomainException($"Current page must be between 0 and {TotalPages}.");

        CurrentPage = newPage;
        Percentage = PercentageOf(newPage, TotalPages);
        LastReadAt = DateTimeOffset.UtcNow;

        if (newPage == TotalPages)
            MarkFinished();
        else
            MarkUnfinished();
    }

    /// <summary>
    /// Re-syncs the snapshot page count with the book's live PageCount (e.g. after an
    /// edition correction), clamps the current page, and re-derives completion status.
    /// </summary>
    public void ReconcileTotalPages(int totalPages)
    {
        if (totalPages <= 0)
            throw new ReadingDomainException("Total pages must be greater than zero.");

        if (TotalPages == totalPages) return;

        TotalPages = totalPages;
        if (CurrentPage > totalPages) CurrentPage = totalPages;
        Percentage = PercentageOf(CurrentPage, totalPages);
        LastReadAt = DateTimeOffset.UtcNow;

        if (CurrentPage == totalPages)
            MarkFinished();
        else
            MarkUnfinished();
    }

    /// <summary>
    /// Marks the book finished. Returns true only on the not-completed -&gt; completed edge,
    /// so callers can react exactly once (idempotent).
    /// </summary>
    public bool MarkFinished()
    {
        if (CompletedAt != null) return false;

        CompletedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new BookCompletedEvent(UserId, BookId, CompletedAt.Value));
        return true;
    }

    /// <summary>
    /// Clears the completion marker. Returns true only on the completed -&gt; not-completed edge.
    /// </summary>
    public bool MarkUnfinished()
    {
        if (CompletedAt == null) return false;

        CompletedAt = null;
        return true;
    }

    private static decimal PercentageOf(int currentPage, int totalPages)
        => Math.Round(((decimal)currentPage / totalPages) * 100, 2);
}

public class ReadingHistory : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public Guid BookId { get; private set; }
    public ReadingHistoryAction Action { get; private set; }
    public int DeltaPages { get; private set; }
    public int PreviousPage { get; private set; }
    public int NewPage { get; private set; }
    public DateTimeOffset Timestamp { get; private set; } = DateTimeOffset.UtcNow;

    public User User { get; private set; } = null!;
    public Book Book { get; private set; } = null!;

    private ReadingHistory() { }

    public static ReadingHistory Record(
        Guid userId,
        Guid bookId,
        ReadingHistoryAction action,
        int previousPage,
        int newPage)
    {
        return new ReadingHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BookId = bookId,
            Action = action,
            PreviousPage = previousPage,
            NewPage = newPage,
            DeltaPages = Math.Max(0, newPage - previousPage),
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}

public class ReadingGoal : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public int Year { get; private set; }
    public int TargetBooks { get; private set; }
    public int CompletedBooks { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public User User { get; private set; } = null!;

    private ReadingGoal() { }

    public static ReadingGoal Create(Guid userId, int year, int targetBooks)
    {
        if (targetBooks <= 0)
            throw new ReadingDomainException("Target books must be greater than zero.");
        if (year < 2000 || year > 2100)
            throw new ReadingDomainException("Invalid year specified for reading goal.");

        return new ReadingGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Year = year,
            TargetBooks = targetBooks,
            CompletedBooks = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateTarget(int targetBooks)
    {
        if (targetBooks <= 0)
            throw new ReadingDomainException("Target books must be greater than zero.");

        TargetBooks = targetBooks;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void IncrementCompleted()
    {
        CompletedBooks++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DecrementCompleted()
    {
        if (CompletedBooks > 0)
        {
            CompletedBooks--;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
