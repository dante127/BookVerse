using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Domain;

public class UserBookTests
{
    [Fact]
    public void TransitionStatus_ToCompleted_SetsCompletedAt()
    {
        var entry = UserBook.Create(Guid.NewGuid(), Guid.NewGuid(), UserBookStatus.Reading);

        entry.TransitionStatus(UserBookStatus.Completed);

        entry.CompletedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(UserBookStatus.Reading)]
    [InlineData(UserBookStatus.Paused)]
    [InlineData(UserBookStatus.Dropped)]
    [InlineData(UserBookStatus.WantToRead)]
    public void TransitionStatus_AwayFromCompleted_ClearsCompletedAt(UserBookStatus target)
    {
        var entry = UserBook.Create(Guid.NewGuid(), Guid.NewGuid(), UserBookStatus.Completed);
        entry.CompletedAt.Should().NotBeNull();

        entry.TransitionStatus(target);

        entry.Status.Should().Be(target);
        entry.CompletedAt.Should().BeNull("a book that is no longer completed must not keep a stale completion date");
    }
}
