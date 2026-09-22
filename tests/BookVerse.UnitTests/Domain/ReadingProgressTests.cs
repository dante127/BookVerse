using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Domain;

public class ReadingProgressTests
{
    [Fact]
    public void Create_WithValidPage_ShouldCalculatePercentage()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Act
        var progress = ReadingProgress.Create(userId, bookId, 400, 100);

        // Assert
        progress.CurrentPage.Should().Be(100);
        progress.TotalPages.Should().Be(400);
        progress.Percentage.Should().Be(25.00m);
        progress.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WhenInitialPageEqualsTotalPages_ShouldCompleteAndRaiseBookCompletedEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Act
        var progress = ReadingProgress.Create(userId, bookId, 300, 300);

        // Assert
        progress.Percentage.Should().Be(100.00m);
        progress.CompletedAt.Should().NotBeNull();
        progress.DomainEvents.Should().ContainSingle(e => e is BookCompletedEvent);
    }

    [Fact]
    public void Create_WhenPageExceedsTotal_ShouldThrowReadingDomainException()
    {
        // Act
        var act = () => ReadingProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 200, 250);

        // Assert
        act.Should().Throw<ReadingDomainException>()
            .WithMessage("*must be between 0 and 200*");
    }

    [Fact]
    public void UpdateProgress_WhenReachingLastPage_ShouldMarkCompletedAndEmitEvent()
    {
        // Arrange
        var progress = ReadingProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 500, 250);

        // Act
        progress.UpdateProgress(500);

        // Assert
        progress.CurrentPage.Should().Be(500);
        progress.Percentage.Should().Be(100.00m);
        progress.CompletedAt.Should().NotBeNull();
        progress.DomainEvents.Should().ContainSingle(e => e is BookCompletedEvent);
    }

    [Fact]
    public void UpdateProgress_WhenExceedingTotalPages_ShouldThrowReadingDomainException()
    {
        // Arrange
        var progress = ReadingProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 500, 250);

        // Act
        var act = () => progress.UpdateProgress(501);

        // Assert
        act.Should().Throw<ReadingDomainException>();
    }
}
