using BookVerse.Domain.Enums;

namespace BookVerse.Application.Features.Books;

public record BookSummaryDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? ISBN,
    int PageCount,
    string Language,
    string? Publisher,
    string? CoverImageUrl,
    decimal AverageRating,
    int RatingsCount,
    int ReviewsCount,
    BookStatus Status,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres);

public record BookDetailDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string Description,
    string? ISBN,
    DateOnly? PublicationDate,
    int PageCount,
    string Language,
    string? Publisher,
    string? CoverImageUrl,
    decimal AverageRating,
    int RatingsCount,
    int ReviewsCount,
    BookStatus Status,
    IReadOnlyList<BookAuthorDto> Authors,
    IReadOnlyList<BookGenreDto> Genres,
    IReadOnlyList<string> Tags,
    IReadOnlyList<BookEditionDto> Editions);

public record BookAuthorDto(Guid AuthorId, string Name, AuthorRole Role, int OrderIndex);
public record BookGenreDto(Guid GenreId, string Name, string Slug);
public record BookEditionDto(
    Guid Id,
    string ISBN,
    BookEditionFormat Format,
    string? Publisher,
    DateOnly? PublicationDate,
    int PageCount,
    string Language,
    long? FileSizeInBytes,
    string? FileUrl);
