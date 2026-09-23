using BookVerse.Domain.Entities.Books;

namespace BookVerse.Application.Features.Books;

/// <summary>
/// CQ-05: Book was projected by hand in two places with shapes that could drift.
/// All Book -> DTO mapping is owned here.
/// </summary>
public static class BookMapper
{
    public static BookSummaryDto ToSummaryDto(Book book) => new(
        book.Id,
        book.Title,
        book.Subtitle,
        book.ISBN,
        book.PageCount,
        book.Language,
        book.Publisher,
        book.CoverImageUrl,
        book.AverageRating,
        book.RatingsCount,
        book.ReviewsCount,
        book.Status,
        book.Authors.OrderBy(a => a.OrderIndex).Select(a => a.Author.Name).ToList(),
        book.Genres.Select(g => g.Genre.Name).ToList());

    public static BookDetailDto ToDetailDto(Book book) => new(
        book.Id,
        book.Title,
        book.Subtitle,
        book.Description,
        book.ISBN,
        book.PublicationDate,
        book.PageCount,
        book.Language,
        book.Publisher,
        book.CoverImageUrl,
        book.AverageRating,
        book.RatingsCount,
        book.ReviewsCount,
        book.Status,
        book.Authors.OrderBy(a => a.OrderIndex).Select(a => new BookAuthorDto(a.AuthorId, a.Author.Name, a.Role, a.OrderIndex)).ToList(),
        book.Genres.Select(g => new BookGenreDto(g.GenreId, g.Genre.Name, g.Genre.Slug)).ToList(),
        book.Tags.Select(t => t.Tag.Name).ToList(),
        book.Editions.Select(e => new BookEditionDto(e.Id, e.ISBN, e.Format, e.Publisher, e.PublicationDate, e.PageCount, e.Language, e.FileSizeInBytes, e.FileUrl)).ToList());
}
