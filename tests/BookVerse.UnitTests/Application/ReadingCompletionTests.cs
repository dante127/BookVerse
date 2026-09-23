using BookVerse.Application.Common.EventHandlers;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Features.Library;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BookVerse.UnitTests.Application;

public class ReadingCompletionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ReadingCompletionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    private async Task<(Guid UserId, Book Book, ReadingGoal Goal)> SeedAsync(int pageCount = 300)
    {
        var user = User.Create($"user-{Guid.NewGuid():N}@test.io", "hash", "salt");
        var book = Book.Create("Coordinated Book", "Description", pageCount);
        var goal = ReadingGoal.Create(user.Id, DateTime.UtcNow.Year, 5);
        _context.Users.Add(user);
        _context.Books.Add(book);
        _context.ReadingGoals.Add(goal);
        await _context.SaveChangesAsync();
        return (user.Id, book, goal);
    }

    [Fact]
    public async Task SyncCompletionStatus_WhenNoProgressExists_ShouldCreateFullProgressAndCountGoalOnce()
    {
        var (userId, book, goal) = await SeedAsync();

        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: true);
        await _context.SaveChangesAsync();

        // Second completion in a later save scope must not double-count.
        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: true);
        await _context.SaveChangesAsync();

        goal.CompletedBooks.Should().Be(1, "setting completed twice must not double-count");
        var progress = await _context.ReadingProgresses.SingleAsync(p => p.UserId == userId && p.BookId == book.Id);
        progress.CurrentPage.Should().Be(book.PageCount);
        progress.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SyncCompletionStatus_UnCompleting_ShouldDecrementGoal()
    {
        var (userId, book, goal) = await SeedAsync();
        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: true);
        await _context.SaveChangesAsync();

        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: false);
        await _context.SaveChangesAsync();

        goal.CompletedBooks.Should().Be(0);
        var progress = await _context.ReadingProgresses.SingleAsync(p => p.UserId == userId && p.BookId == book.Id);
        progress.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task SyncCompletionStatus_WhenUncompletedAndNoProgress_ShouldNotCreateOrphanRows()
    {
        var (userId, book, goal) = await SeedAsync();

        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: false);
        await _context.SaveChangesAsync();

        goal.CompletedBooks.Should().Be(0);
        (await _context.ReadingProgresses.AnyAsync(p => p.UserId == userId && p.BookId == book.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SyncCompletionStatus_ShouldReconcileStalePageCountSnapshot()
    {
        var (userId, book, _) = await SeedAsync(pageCount: 300);
        _context.ReadingProgresses.Add(ReadingProgress.Create(userId, book.Id, 250, 250));
        await _context.SaveChangesAsync();

        var goal = await _context.ReadingGoals.SingleAsync(g => g.UserId == userId);
        goal.CompletedBooks.Should().Be(0, "the stale snapshot said completed, but reconciliation runs before the edge check");

        // BL-05: the book now has 300 pages, so the 250-page snapshot no longer means "finished".
        await ReadingCompletion.SyncCompletionStatusAsync(_context, userId, book, completed: false);
        await _context.SaveChangesAsync();

        var progress = await _context.ReadingProgresses.SingleAsync(p => p.UserId == userId && p.BookId == book.Id);
        progress.TotalPages.Should().Be(300);
        progress.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RemoveBookFromLibrary_ShouldDeleteOwnedRowsAndReleaseGoalSlot()
    {
        var (userId, book, goal) = await SeedAsync();
        var userBook = UserBook.Create(userId, book.Id, UserBookStatus.Completed);
        _context.UserBooks.Add(userBook);
        _context.FavoriteBooks.Add(new FavoriteBook(userId, book.Id));
        _context.ReadingProgresses.Add(ReadingProgress.Create(userId, book.Id, book.PageCount, book.PageCount));
        _context.ReadingHistories.Add(ReadingHistory.Record(userId, book.Id, ReadingHistoryAction.CompletedBook, 250, 300));
        goal.IncrementCompleted();
        await _context.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.UserId).Returns(userId);

        var handler = new RemoveBookFromLibraryCommandHandler(_context, currentUser.Object);
        var result = await handler.Handle(new RemoveBookFromLibraryCommand(book.Id), CancellationToken.None);

        result.Should().BeTrue();
        goal.CompletedBooks.Should().Be(0);
        (await _context.UserBooks.AnyAsync(ub => ub.UserId == userId)).Should().BeFalse();
        (await _context.FavoriteBooks.AnyAsync(f => f.UserId == userId)).Should().BeFalse();
        (await _context.ReadingProgresses.AnyAsync(p => p.UserId == userId)).Should().BeFalse();
        (await _context.ReadingHistories.AnyAsync(h => h.UserId == userId)).Should().BeFalse();
    }

    [Fact]
    public async Task BookCompletedHandler_ShouldCreateNotificationWithBookTitle()
    {
        var (userId, book, _) = await SeedAsync();

        await new BookCompletedNotificationHandler(_context)
            .Handle(new BookCompletedEvent(userId, book.Id, DateTimeOffset.UtcNow), CancellationToken.None);

        var notification = await _context.Notifications.SingleAsync(n => n.UserId == userId);
        notification.Type.Should().Be(NotificationType.BookCompleted);
        notification.Message.Should().Contain(book.Title);
    }

    [Fact]
    public async Task ReviewApprovedHandler_ShouldCreateNotificationForAuthor()
    {
        var (userId, book, _) = await SeedAsync();

        await new ReviewApprovedNotificationHandler(_context)
            .Handle(new ReviewApprovedEvent(Guid.NewGuid(), book.Id, userId, 5), CancellationToken.None);

        var notification = await _context.Notifications.SingleAsync(n => n.UserId == userId);
        notification.Type.Should().Be(NotificationType.ReviewApproved);
        notification.Message.Should().Contain(book.Title);
    }

    [Fact]
    public async Task ReviewRejectedHandler_ShouldCreateNotificationWithReason()
    {
        var (userId, book, _) = await SeedAsync();

        await new ReviewRejectedNotificationHandler(_context)
            .Handle(new ReviewRejectedEvent(Guid.NewGuid(), book.Id, userId, "Contains spam"), CancellationToken.None);

        var notification = await _context.Notifications.SingleAsync(n => n.UserId == userId);
        notification.Type.Should().Be(NotificationType.ReviewRejected);
        notification.Message.Should().Contain("Contains spam");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
