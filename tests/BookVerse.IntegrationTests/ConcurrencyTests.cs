using BookVerse.Domain.Entities.Books;
using BookVerse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookVerse.IntegrationTests;

public class ConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var initContext = new ApplicationDbContext(_options);
        initContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task ConcurrentRatingUpdates_WhenRowVersionMismatch_ShouldThrowDbUpdateConcurrencyException()
    {
        // 1. Seed a book in initial state
        var bookId = Guid.NewGuid();
        using (var seedContext = new ApplicationDbContext(_options))
        {
            var book = Book.Create("Concurrent Book", "Description", 300);
            typeof(Book).GetProperty("Id")!.SetValue(book, bookId);
            seedContext.Books.Add(book);
            await seedContext.SaveChangesAsync();
        }

        // 2. Load the same book in two distinct DbContext instances (User A and User B)
        using var contextUserA = new ApplicationDbContext(_options);
        using var contextUserB = new ApplicationDbContext(_options);

        var bookUserA = await contextUserA.Books.FirstAsync(b => b.Id == bookId);
        var bookUserB = await contextUserB.Books.FirstAsync(b => b.Id == bookId);

        // 3. User A modifies and saves first
        bookUserA.ApplyNewRating(5);
        await contextUserA.SaveChangesAsync();

        // 4. User B attempts to save stale entity (simulating concurrency collision)
        // Manually simulate a stale RowVersion on user B to trigger concurrency check
        contextUserB.Entry(bookUserB).Property("RowVersion").OriginalValue = new byte[] { 0, 0, 0, 0, 0, 0, 0, 99 };
        bookUserB.ApplyNewRating(4);

        var act = async () => await contextUserB.SaveChangesAsync();

        // 5. Assert: Concurrency conflict detected!
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
