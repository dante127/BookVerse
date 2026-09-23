using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Domain;

public class BookTests
{
    [Fact]
    public void Create_WithValidInputs_ShouldInitializeInDraftState()
    {
        // Act
        var book = Book.Create(
            "Mistborn: The Final Empire",
            "In a world where ash falls from the sky and mist dominates the night...",
            541,
            "Book 1",
            "9780765350381");

        // Assert
        book.Title.Should().Be("Mistborn: The Final Empire");
        book.PageCount.Should().Be(541);
        book.Status.Should().Be(BookStatus.Draft);
        book.AverageRating.Should().Be(0.00m);
        book.RatingsCount.Should().Be(0);
        book.ReviewsCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyTitle_ShouldThrowBookDomainException(string invalidTitle)
    {
        // Act
        var act = () => Book.Create(invalidTitle, "Description", 300);

        // Assert
        act.Should().Throw<BookDomainException>()
            .WithMessage("*title cannot be empty*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_WithInvalidPageCount_ShouldThrowBookDomainException(int invalidPageCount)
    {
        // Act
        var act = () => Book.Create("Valid Title", "Description", invalidPageCount);

        // Assert
        act.Should().Throw<BookDomainException>()
            .WithMessage("*page count must be greater than zero*");
    }

    [Fact]
    public void Publish_WhenDraft_ShouldTransitionToPublishedAndRaiseEvent()
    {
        // Arrange
        var book = Book.Create("Dune", "A science fiction epic set on the desert planet Arrakis.", 412);
        var author = Author.Create("Frank Herbert", "frank-herbert");
        var genre = Genre.Create("Science Fiction", "sci-fi");

        book.AddAuthor(author, AuthorRole.Author);
        book.AddGenre(genre);

        // Act
        book.Publish();

        // Assert
        book.Status.Should().Be(BookStatus.Published);
        book.DomainEvents.Should().ContainSingle(e => e is BookPublishedEvent);

        var publishedEvent = book.DomainEvents.OfType<BookPublishedEvent>().First();
        publishedEvent.BookId.Should().Be(book.Id);
        publishedEvent.Title.Should().Be("Dune");
        publishedEvent.AuthorIds.Should().Contain(author.Id);
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_ShouldThrowBookDomainException()
    {
        // Arrange
        var book = Book.Create("Dune", "Description", 412);
        book.Publish();

        // Act
        var act = () => book.Publish();

        // Assert
        act.Should().Throw<BookDomainException>()
            .WithMessage("*already published*");
    }

    [Fact]
    public void ApplyNewRating_ShouldCorrectlyUpdateRunningAverageAndCount()
    {
        // Arrange
        var book = Book.Create("Hyperion", "A legendary space opera.", 482);

        // Act 1
        book.ApplyNewRating(5);

        // Assert 1
        book.RatingsCount.Should().Be(1);
        book.AverageRating.Should().Be(5.00m);

        // Act 2
        book.ApplyNewRating(3);

        // Assert 2: (5 + 3) / 2 = 4.00
        book.RatingsCount.Should().Be(2);
        book.AverageRating.Should().Be(4.00m);

        // Act 3
        book.ApplyNewRating(4);

        // Assert 3: (5 + 3 + 4) / 3 = 4.00
        book.RatingsCount.Should().Be(3);
        book.AverageRating.Should().Be(4.00m);
    }

    [Fact]
    public void UpdateExistingRating_ShouldRecalculateAverage()
    {
        // Arrange
        var book = Book.Create("Foundation", "Galactic empire collapse.", 255);
        book.ApplyNewRating(5);
        book.ApplyNewRating(5); // Average 5.0, count 2

        // Act: change one 5 to 1 -> sum = 6 / 2 = 3.00
        book.UpdateExistingRating(5, 1);

        // Assert
        book.RatingsCount.Should().Be(2);
        book.AverageRating.Should().Be(3.00m);
    }

    [Fact]
    public void RemoveRating_ShouldReduceCountAndRecalculate()
    {
        // Arrange
        var book = Book.Create("Foundation", "Description", 255);
        book.ApplyNewRating(4);
        book.ApplyNewRating(2); // Avg = 3.00, count = 2

        // Act
        book.RemoveRating(2);

        // Assert
        book.RatingsCount.Should().Be(1);
        book.AverageRating.Should().Be(4.00m);
    }

    [Fact]
    public void RecalculateRatingAggregates_ShouldOverwriteBothAggregates()
    {
        // Arrange
        var book = Book.Create("Neuromancer", "Winter data.", 271);
        book.ApplyNewRating(5);
        book.ApplyNewRating(5); // Drifted state: avg 5.00, count 2

        // Act
        book.RecalculateRatingAggregates(3.333m, 7);

        // Assert: rounding to 2 decimals and full overwrite of the count
        book.RatingsCount.Should().Be(7);
        book.AverageRating.Should().Be(3.33m);
    }

    [Fact]
    public void RecalculateRatingAggregates_ShouldResetToZero_WhenNoPublishedReviewsRemain()
    {
        var book = Book.Create("Neuromancer", "Winter data.", 271);
        book.ApplyNewRating(4);

        book.RecalculateRatingAggregates(0m, 0);

        book.RatingsCount.Should().Be(0);
        book.AverageRating.Should().Be(0m);
    }

    [Theory]
    [InlineData(6.0, 1)]
    [InlineData(-0.01, 1)]
    [InlineData(3.0, -1)]
    public void RecalculateRatingAggregates_ShouldRejectOutOfRangeValues(decimal avg, int count)
    {
        var book = Book.Create("Neuromancer", "Winter data.", 271);

        var act = () => book.RecalculateRatingAggregates(avg, count);

        act.Should().Throw<BookDomainException>();
    }
}
