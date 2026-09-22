using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Domain;

public class ReviewTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_WithValidRating_ShouldInitializeSuccessfully(int validRating)
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var review = BookReview.Create(bookId, userId, validRating, "Headline", "Great book");

        // Assert
        review.Rating.Should().Be(validRating);
        review.Status.Should().Be(ReviewStatus.Pending);
        review.DomainEvents.Should().ContainSingle(e => e is ReviewCreatedEvent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Create_WithInvalidRating_ShouldThrowReviewDomainException(int invalidRating)
    {
        // Act
        var act = () => BookReview.Create(Guid.NewGuid(), Guid.NewGuid(), invalidRating, "Title", "Content");

        // Assert
        act.Should().Throw<ReviewDomainException>()
            .WithMessage("*must be an integer between 1 and 5*");
    }

    [Fact]
    public void Approve_WhenPending_ShouldTransitionToPublishedAndRaiseApprovedEvent()
    {
        // Arrange
        var review = BookReview.Create(Guid.NewGuid(), Guid.NewGuid(), 4, "Title", "Content");
        review.ClearDomainEvents();
        var moderatorId = Guid.NewGuid();

        // Act
        review.Approve(moderatorId);

        // Assert
        review.Status.Should().Be(ReviewStatus.Published);
        review.ModeratedBy.Should().Be(moderatorId);
        review.ModeratedAt.Should().NotBeNull();
        review.DomainEvents.Should().ContainSingle(e => e is ReviewApprovedEvent);
    }

    [Fact]
    public void Reject_ShouldTransitionToRejectedAndRecordNote()
    {
        // Arrange
        var review = BookReview.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "Spam", "Click here for free bitcoin");
        review.ClearDomainEvents();
        var moderatorId = Guid.NewGuid();

        // Act
        review.Reject(moderatorId, "Contains commercial spam links.");

        // Assert
        review.Status.Should().Be(ReviewStatus.Rejected);
        review.ModerationNote.Should().Be("Contains commercial spam links.");
        review.DomainEvents.Should().ContainSingle(e => e is ReviewRejectedEvent);
    }
}
