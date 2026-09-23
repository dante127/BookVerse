using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Entities.Tags;
using BookVerse.Domain.Enums;
using BookVerse.Domain.Events;
using BookVerse.Domain.Exceptions;

namespace BookVerse.Domain.Entities.Books;

public class Book : AuditableEntity<Guid>
{
    private readonly List<BookEdition> _editions = [];
    private readonly List<BookAuthor> _authors = [];
    private readonly List<BookGenre> _genres = [];
    private readonly List<BookTag> _tags = [];

    public string Title { get; private set; } = string.Empty;
    public string? Subtitle { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? ISBN { get; private set; }
    public DateOnly? PublicationDate { get; private set; }
    public int PageCount { get; private set; }
    public string Language { get; private set; } = "en";
    public string? Publisher { get; private set; }
    public string? CoverImageUrl { get; private set; }
    public decimal AverageRating { get; private set; } = 0.00m;
    public int RatingsCount { get; private set; } = 0;
    public int ReviewsCount { get; private set; } = 0;
    public BookStatus Status { get; private set; } = BookStatus.Draft;
    public byte[] RowVersion { get; private set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public IReadOnlyCollection<BookEdition> Editions => _editions.AsReadOnly();
    public IReadOnlyCollection<BookAuthor> Authors => _authors.AsReadOnly();
    public IReadOnlyCollection<BookGenre> Genres => _genres.AsReadOnly();
    public IReadOnlyCollection<BookTag> Tags => _tags.AsReadOnly();

    private Book() { }

    public static Book Create(
        string title,
        string description,
        int pageCount,
        string? subtitle = null,
        string? isbn = null,
        DateOnly? publicationDate = null,
        string language = "en",
        string? publisher = null,
        string? coverImageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new BookDomainException("Book title cannot be empty.");
        if (pageCount <= 0)
            throw new BookDomainException("Book page count must be greater than zero.");

        return new Book
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Subtitle = subtitle?.Trim(),
            Description = description.Trim(),
            ISBN = string.IsNullOrWhiteSpace(isbn) ? null : isbn.Trim(),
            PublicationDate = publicationDate,
            PageCount = pageCount,
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant(),
            Publisher = publisher?.Trim(),
            CoverImageUrl = coverImageUrl?.Trim(),
            AverageRating = 0.00m,
            RatingsCount = 0,
            ReviewsCount = 0,
            Status = BookStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        string title,
        string description,
        int pageCount,
        string? subtitle,
        string? isbn,
        DateOnly? publicationDate,
        string language,
        string? publisher,
        string? coverImageUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new BookDomainException("Book title cannot be empty.");
        if (pageCount <= 0)
            throw new BookDomainException("Book page count must be greater than zero.");

        Title = title.Trim();
        Subtitle = subtitle?.Trim();
        Description = description.Trim();
        ISBN = string.IsNullOrWhiteSpace(isbn) ? null : isbn.Trim();
        PublicationDate = publicationDate;
        PageCount = pageCount;
        Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
        Publisher = publisher?.Trim();
        CoverImageUrl = coverImageUrl?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Publish()
    {
        if (Status == BookStatus.Published)
            throw new BookDomainException("Book is already published.");

        if (Status == BookStatus.Archived)
            throw new BookDomainException("Archived books cannot be directly published.");

        Status = BookStatus.Published;
        UpdatedAt = DateTimeOffset.UtcNow;

        var authorIds = _authors.Select(a => a.AuthorId).ToList();
        var primaryGenre = _genres.FirstOrDefault()?.GenreId;

        AddDomainEvent(new BookPublishedEvent(Id, Title, authorIds, primaryGenre));
    }

    public void AddAuthor(Author author, AuthorRole role, int orderIndex = 0)
    {
        if (_authors.Any(a => a.AuthorId == author.Id && a.Role == role)) return;
        _authors.Add(new BookAuthor(Id, author.Id, role, orderIndex));
    }

    public void RemoveAuthor(Guid authorId)
    {
        _authors.RemoveAll(a => a.AuthorId == authorId);
    }

    public void AddGenre(Genre genre)
    {
        if (_genres.Any(g => g.GenreId == genre.Id)) return;
        _genres.Add(new BookGenre(Id, genre.Id));
    }

    public void RemoveGenre(Guid genreId)
    {
        _genres.RemoveAll(g => g.GenreId == genreId);
    }

    public void AddTag(Tag tag)
    {
        if (_tags.Any(t => t.TagId == tag.Id)) return;
        _tags.Add(new BookTag(Id, tag.Id));
    }

    public void RemoveTag(Guid tagId)
    {
        _tags.RemoveAll(t => t.TagId == tagId);
    }

    public BookEdition AddEdition(
        string isbn,
        BookEditionFormat format,
        string? publisher = null,
        DateOnly? publicationDate = null,
        int? pageCount = null,
        string? language = null,
        long? fileSizeInBytes = null,
        string? fileUrl = null)
    {
        var edition = BookEdition.Create(
            Id,
            isbn,
            format,
            publisher ?? Publisher,
            publicationDate ?? PublicationDate,
            pageCount ?? PageCount,
            language ?? Language,
            fileSizeInBytes,
            fileUrl);

        _editions.Add(edition);
        return edition;
    }

    public void ApplyNewRating(int rating)
    {
        if (rating < 1 || rating > 5)
            throw new BookDomainException("Rating must be between 1 and 5.");

        var totalScore = (AverageRating * RatingsCount) + rating;
        RatingsCount++;
        AverageRating = Math.Round(totalScore / RatingsCount, 2);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateExistingRating(int oldRating, int newRating)
    {
        if (newRating < 1 || newRating > 5)
            throw new BookDomainException("Rating must be between 1 and 5.");
        if (RatingsCount <= 0)
        {
            ApplyNewRating(newRating);
            return;
        }

        var totalScore = (AverageRating * RatingsCount) - oldRating + newRating;
        AverageRating = Math.Round(totalScore / RatingsCount, 2);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecalculateRatingAggregates(decimal averageRating, int ratingsCount)
    {
        if (ratingsCount < 0)
            throw new BookDomainException("Ratings count cannot be negative.");
        if (averageRating < 0 || averageRating > 5)
            throw new BookDomainException("Average rating must be between 0 and 5.");

        RatingsCount = ratingsCount;
        AverageRating = Math.Round(averageRating, 2);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveRating(int oldRating)
    {
        if (RatingsCount <= 1)
        {
            RatingsCount = 0;
            AverageRating = 0.00m;
        }
        else
        {
            var totalScore = (AverageRating * RatingsCount) - oldRating;
            RatingsCount--;
            AverageRating = Math.Round(totalScore / RatingsCount, 2);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void IncrementReviewCount() => ReviewsCount++;
    public void DecrementReviewCount() { if (ReviewsCount > 0) ReviewsCount--; }
}

public class BookEdition : Entity<Guid>
{
    public Guid BookId { get; private set; }
    public string ISBN { get; private set; } = string.Empty;
    public BookEditionFormat Format { get; private set; }
    public string? Publisher { get; private set; }
    public DateOnly? PublicationDate { get; private set; }
    public int PageCount { get; private set; }
    public string Language { get; private set; } = "en";
    public long? FileSizeInBytes { get; private set; }
    public string? FileUrl { get; private set; }

    public Book Book { get; private set; } = null!;

    private BookEdition() { }

    public static BookEdition Create(
        Guid bookId,
        string isbn,
        BookEditionFormat format,
        string? publisher,
        DateOnly? publicationDate,
        int pageCount,
        string language,
        long? fileSizeInBytes,
        string? fileUrl)
    {
        if (string.IsNullOrWhiteSpace(isbn))
            throw new BookDomainException("Edition ISBN cannot be empty.");

        return new BookEdition
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            ISBN = isbn.Trim(),
            Format = format,
            Publisher = publisher?.Trim(),
            PublicationDate = publicationDate,
            PageCount = pageCount > 0 ? pageCount : 1,
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant(),
            FileSizeInBytes = fileSizeInBytes,
            FileUrl = fileUrl?.Trim()
        };
    }
}

public class BookAuthor
{
    public Guid BookId { get; private set; }
    public Guid AuthorId { get; private set; }
    public AuthorRole Role { get; private set; }
    public int OrderIndex { get; private set; }

    public Book Book { get; private set; } = null!;
    public Author Author { get; private set; } = null!;

    private BookAuthor() { }

    public BookAuthor(Guid bookId, Guid authorId, AuthorRole role, int orderIndex = 0)
    {
        BookId = bookId;
        AuthorId = authorId;
        Role = role;
        OrderIndex = orderIndex;
    }
}

public class BookGenre
{
    public Guid BookId { get; private set; }
    public Guid GenreId { get; private set; }

    public Book Book { get; private set; } = null!;
    public Genre Genre { get; private set; } = null!;

    private BookGenre() { }

    public BookGenre(Guid bookId, Guid genreId)
    {
        BookId = bookId;
        GenreId = genreId;
    }
}

public class BookTag
{
    public Guid BookId { get; private set; }
    public Guid TagId { get; private set; }

    public Book Book { get; private set; } = null!;
    public Tag Tag { get; private set; } = null!;

    private BookTag() { }

    public BookTag(Guid bookId, Guid tagId)
    {
        BookId = bookId;
        TagId = tagId;
    }
}
