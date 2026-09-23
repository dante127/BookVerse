using BookVerse.Domain.Common;

namespace BookVerse.Domain.Events;

public record BookPublishedEvent(
    Guid BookId,
    string Title,
    IReadOnlyList<Guid> AuthorIds,
    Guid? PrimaryGenreId) : IDomainEvent;

public record ReviewCreatedEvent(
    Guid ReviewId,
    Guid BookId,
    Guid UserId,
    int Rating,
    string? Title) : IDomainEvent;

public record ReviewApprovedEvent(
    Guid ReviewId,
    Guid BookId,
    Guid UserId,
    int Rating) : IDomainEvent;

public record ReviewRejectedEvent(
    Guid ReviewId,
    Guid BookId,
    Guid UserId,
    string Reason) : IDomainEvent;

public record BookCompletedEvent(
    Guid UserId,
    Guid BookId,
    DateTimeOffset CompletedAt) : IDomainEvent;
