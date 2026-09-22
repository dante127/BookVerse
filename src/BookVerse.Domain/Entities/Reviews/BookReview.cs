using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;

namespace BookVerse.Domain.Entities.Reviews;

public class BookReview : AggregateRoot<Guid>
{
    public Guid BookId { get; private set; }
    public Guid UserId { get; private set; }
    public int Rating { get; private set; }
    public string? Title { get; private set; }
    public string? Content { get; private set; }
    public ReviewStatus Status { get; private set; } = ReviewStatus.Pending;
    public string? ModerationNote { get; private set; }
    public Guid? ModeratedBy { get; private set; }
    public DateTimeOffset? ModeratedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [0, 0, 0, 0, 0, 0, 0, 1];
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public Book Book { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private BookReview() { }

    public static BookReview Create(
        Guid bookId,
        Guid userId,
        int rating,
        string? title,
        string? content,
        ReviewStatus initialStatus = ReviewStatus.Pending)
    {
        if (rating < 1 || rating > 5)
            throw new ReviewDomainException("Rating must be an integer between 1 and 5.");

        var review = new BookReview
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            UserId = userId,
            Rating = rating,
            Title = title?.Trim(),
            Content = content?.Trim(),
            Status = initialStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };

        review.AddDomainEvent(new ReviewCreatedEvent(review.Id, bookId, userId, rating, title));

        if (initialStatus == ReviewStatus.Published)
        {
            review.AddDomainEvent(new ReviewApprovedEvent(review.Id, bookId, userId, rating));
        }

        return review;
    }

    public void Update(int rating, string? title, string? content)
    {
        if (rating < 1 || rating > 5)
            throw new ReviewDomainException("Rating must be an integer between 1 and 5.");

        Rating = rating;
        Title = title?.Trim();
        Content = content?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(Guid moderatorId)
    {
        Status = ReviewStatus.Published;
        ModeratedBy = moderatorId;
        ModeratedAt = DateTimeOffset.UtcNow;
        ModerationNote = null;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new ReviewApprovedEvent(Id, BookId, UserId, Rating));
    }

    public void Reject(Guid moderatorId, string reason)
    {
        Status = ReviewStatus.Rejected;
        ModeratedBy = moderatorId;
        ModeratedAt = DateTimeOffset.UtcNow;
        ModerationNote = reason;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new ReviewRejectedEvent(Id, BookId, UserId, reason));
    }

    public void Hide(Guid moderatorId)
    {
        Status = ReviewStatus.Hidden;
        ModeratedBy = moderatorId;
        ModeratedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        Status = ReviewStatus.Published;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
