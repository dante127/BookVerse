using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Notifications;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Common.EventHandlers;

// BL-06: domain events raised by aggregates are turned into in-app notifications here.
// These handlers run after the originating SaveChanges (dispatch interceptor) and create
// Notification rows, which raise no events themselves — so there is no recursion.

public class BookCompletedNotificationHandler : INotificationHandler<BookCompletedEvent>
{
    private readonly IApplicationDbContext _context;

    public BookCompletedNotificationHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(BookCompletedEvent notification, CancellationToken cancellationToken)
    {
        var bookTitle = await _context.Books
            .Where(b => b.Id == notification.BookId)
            .Select(b => b.Title)
            .FirstOrDefaultAsync(cancellationToken);

        _context.Notifications.Add(Notification.Create(
            notification.UserId,
            NotificationType.BookCompleted,
            "Book completed",
            $"You finished \"{bookTitle ?? "your book"}\". Well done!",
            $"/library/books/{notification.BookId}"));

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class ReviewApprovedNotificationHandler : INotificationHandler<ReviewApprovedEvent>
{
    private readonly IApplicationDbContext _context;

    public ReviewApprovedNotificationHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(ReviewApprovedEvent notification, CancellationToken cancellationToken)
    {
        var bookTitle = await _context.Books
            .Where(b => b.Id == notification.BookId)
            .Select(b => b.Title)
            .FirstOrDefaultAsync(cancellationToken);

        _context.Notifications.Add(Notification.Create(
            notification.UserId,
            NotificationType.ReviewApproved,
            "Review published",
            $"Your review of \"{bookTitle ?? "a book"}\" is now live.",
            $"/books/{notification.BookId}/reviews"));

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class ReviewRejectedNotificationHandler : INotificationHandler<ReviewRejectedEvent>
{
    private readonly IApplicationDbContext _context;

    public ReviewRejectedNotificationHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(ReviewRejectedEvent notification, CancellationToken cancellationToken)
    {
        _context.Notifications.Add(Notification.Create(
            notification.UserId,
            NotificationType.ReviewRejected,
            "Review not published",
            $"Your review did not pass moderation. Reason: {notification.Reason}",
            $"/books/{notification.BookId}/reviews"));

        await _context.SaveChangesAsync(cancellationToken);
    }
}
