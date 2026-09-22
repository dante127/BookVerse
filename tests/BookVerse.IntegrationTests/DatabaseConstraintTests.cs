using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Enums;
using BookVerse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookVerse.IntegrationTests;

public class DatabaseConstraintTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public DatabaseConstraintTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UniqueUserEmail_WhenDuplicateInserted_ShouldThrowDbUpdateException()
    {
        // Arrange
        var user1 = User.Create("test@bookverse.io", "hash1", "salt1");
        var user2 = User.Create("test@bookverse.io", "hash2", "salt2");

        _context.Users.Add(user1);
        await _context.SaveChangesAsync();

        // Act
        _context.Users.Add(user2);
        var act = async () => await _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UniqueUserBook_WhenDuplicateInserted_ShouldThrowDbUpdateException()
    {
        // Arrange
        var user = User.Create("reader@bookverse.io", "hash", "salt");
        var book = Book.Create("Test Book", "Desc", 300);
        _context.Users.Add(user);
        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        var entry1 = UserBook.Create(user.Id, book.Id, UserBookStatus.WantToRead);
        var entry2 = UserBook.Create(user.Id, book.Id, UserBookStatus.Reading);

        _context.UserBooks.Add(entry1);
        await _context.SaveChangesAsync();

        // Act
        _context.UserBooks.Add(entry2);
        var act = async () => await _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UniqueBookReview_PerUserAndBook_WhenDuplicateInserted_ShouldThrowDbUpdateException()
    {
        // Arrange
        var user = User.Create("reviewer@bookverse.io", "hash", "salt");
        var book = Book.Create("Book to Review", "Desc", 350);
        _context.Users.Add(user);
        _context.Books.Add(book);
        await _context.SaveChangesAsync();

        var review1 = BookReview.Create(book.Id, user.Id, 5, "Great", "Nice book");
        var review2 = BookReview.Create(book.Id, user.Id, 4, "Second Review", "Duplicate attempt");

        _context.BookReviews.Add(review1);
        await _context.SaveChangesAsync();

        // Act
        _context.BookReviews.Add(review2);
        var act = async () => await _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
