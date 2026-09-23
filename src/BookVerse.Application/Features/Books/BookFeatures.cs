using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Enums;
using BookVerse.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

// --- Get Books (Paginated) ---
public record GetBooksQuery(
    Guid? GenreId = null,
    BookStatus? Status = BookStatus.Published,
    string? SortBy = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<BookSummaryDto>>;

public class GetBooksQueryHandler : IRequestHandler<GetBooksQuery, PagedResult<BookSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetBooksQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    private bool CanViewUnpublished =>
        _currentUserService.IsInRole("Admin") || _currentUserService.IsInRole("Moderator");

    public async Task<PagedResult<BookSummaryDto>> Handle(GetBooksQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(request.Page, request.PageSize);
        var query = _context.Books.AsNoTracking();

        // Non-staff callers cannot browse by status: Published is forced so that
        // Draft/Archived rows are never exposed via an empty or spoofed status filter.
        var effectiveStatus = CanViewUnpublished ? request.Status : BookStatus.Published;
        if (effectiveStatus.HasValue)
        {
            query = query.Where(b => b.Status == effectiveStatus.Value);
        }

        if (request.GenreId.HasValue)
        {
            query = query.Where(b => b.Genres.Any(g => g.GenreId == request.GenreId.Value));
        }

        query = request.SortBy?.ToLowerInvariant() switch
        {
            "rating" => query.OrderByDescending(b => b.AverageRating).ThenByDescending(b => b.RatingsCount),
            "popular" => query.OrderByDescending(b => b.RatingsCount),
            "newest" => query.OrderByDescending(b => b.PublicationDate).ThenByDescending(b => b.CreatedAt),
            "title" => query.OrderBy(b => b.Title),
            _ => query.OrderByDescending(b => b.CreatedAt)
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var rawItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Subtitle,
                b.ISBN,
                b.PageCount,
                b.Language,
                b.Publisher,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                b.ReviewsCount,
                b.Status,
                Authors = b.Authors.OrderBy(a => a.OrderIndex).Select(a => a.Author.Name).ToList(),
                Genres = b.Genres.Select(g => g.Genre.Name).ToList()
            })
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(b => new BookSummaryDto(
            b.Id,
            b.Title,
            b.Subtitle,
            b.ISBN,
            b.PageCount,
            b.Language,
            b.Publisher,
            b.CoverImageUrl,
            b.AverageRating,
            b.RatingsCount,
            b.ReviewsCount,
            b.Status,
            b.Authors,
            b.Genres)).ToList();

        return new PagedResult<BookSummaryDto>(items, totalCount, page, pageSize);
    }
}

// --- Get Book by Id ---
public record GetBookByIdQuery(Guid Id) : IRequest<BookDetailDto>;

public class GetBookByIdQueryHandler : IRequestHandler<GetBookByIdQuery, BookDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly ICurrentUserService _currentUserService;

    public GetBookByIdQueryHandler(IApplicationDbContext context, ICacheService cacheService, ICurrentUserService currentUserService)
    {
        _context = context;
        _cacheService = cacheService;
        _currentUserService = currentUserService;
    }

    public async Task<BookDetailDto> Handle(GetBookByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"books:details:{request.Id}";
        var cached = await _cacheService.GetAsync<BookDetailDto>(cacheKey, cancellationToken);
        if (cached != null)
        {
            EnforceVisibility(cached);
            return cached;
        }

        var book = await _context.Books
            .AsNoTracking()
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Genres).ThenInclude(bg => bg.Genre)
            .Include(b => b.Tags).ThenInclude(bt => bt.Tag)
            .Include(b => b.Editions)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (book == null)
        {
            throw new NotFoundException("Book", request.Id);
        }

        var dto = new BookDetailDto(
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

        await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(30), cancellationToken);

        EnforceVisibility(dto);
        return dto;
    }

    private void EnforceVisibility(BookDetailDto dto)
    {
        var isStaff = _currentUserService.IsInRole("Admin") || _currentUserService.IsInRole("Moderator");
        if (!isStaff && dto.Status != BookStatus.Published)
        {
            // Same 404 as a missing book so unpublished entries are not enumerable.
            throw new NotFoundException("Book", dto.Id);
        }
    }
}

// --- Create Book ---
public record CreateBookCommand(
    string Title,
    string Description,
    int PageCount,
    string? Subtitle = null,
    string? ISBN = null,
    DateOnly? PublicationDate = null,
    string Language = "en",
    string? Publisher = null,
    string? CoverImageUrl = null,
    List<AuthorAssignmentDto>? Authors = null,
    List<Guid>? GenreIds = null,
    List<Guid>? TagIds = null) : IRequest<Guid>;

public record AuthorAssignmentDto(Guid AuthorId, AuthorRole Role, int OrderIndex = 0);

public class CreateBookCommandValidator : AbstractValidator<CreateBookCommand>
{
    public CreateBookCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.PageCount).GreaterThan(0).LessThanOrEqualTo(100000);
        RuleFor(x => x.Subtitle).MaximumLength(250);
        RuleFor(x => x.ISBN).IsValidIsbn();
        RuleFor(x => x.Language).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Publisher).MaximumLength(200);
        RuleFor(x => x.CoverImageUrl).IsValidWebUrl();
    }
}

public class CreateBookCommandHandler : IRequestHandler<CreateBookCommand, Guid>
{
    private readonly IApplicationDbContext _context;

    public CreateBookCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateBookCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ISBN))
        {
            var exists = await _context.Books.AnyAsync(b => b.ISBN == request.ISBN.Trim(), cancellationToken);
            if (exists) throw new ConflictException($"A book with ISBN '{request.ISBN}' already exists.");
        }

        var book = Book.Create(
            request.Title,
            request.Description,
            request.PageCount,
            request.Subtitle,
            request.ISBN,
            request.PublicationDate,
            request.Language,
            request.Publisher,
            request.CoverImageUrl);

        if (request.Authors != null)
        {
            foreach (var authorDto in request.Authors)
            {
                var author = await _context.Authors.FindAsync([authorDto.AuthorId], cancellationToken);
                if (author != null)
                {
                    book.AddAuthor(author, authorDto.Role, authorDto.OrderIndex);
                }
            }
        }

        if (request.GenreIds != null)
        {
            foreach (var genreId in request.GenreIds)
            {
                var genre = await _context.Genres.FindAsync([genreId], cancellationToken);
                if (genre != null)
                {
                    book.AddGenre(genre);
                }
            }
        }

        if (request.TagIds != null)
        {
            foreach (var tagId in request.TagIds)
            {
                var tag = await _context.Tags.FindAsync([tagId], cancellationToken);
                if (tag != null)
                {
                    book.AddTag(tag);
                }
            }
        }

        _context.Books.Add(book);
        await _context.SaveChangesAsync(cancellationToken);

        return book.Id;
    }
}

// --- Update Book ---
public record UpdateBookCommand(
    Guid Id,
    string Title,
    string Description,
    int PageCount,
    string? Subtitle,
    string? ISBN,
    DateOnly? PublicationDate,
    string Language,
    string? Publisher,
    string? CoverImageUrl) : IRequest<bool>;

public class UpdateBookCommandValidator : AbstractValidator<UpdateBookCommand>
{
    public UpdateBookCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.PageCount).GreaterThan(0).LessThanOrEqualTo(100000);
        RuleFor(x => x.Subtitle).MaximumLength(250);
        RuleFor(x => x.ISBN).IsValidIsbn();
        RuleFor(x => x.Language).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Publisher).MaximumLength(200);
        RuleFor(x => x.CoverImageUrl).IsValidWebUrl();
    }
}

public class UpdateBookCommandHandler : IRequestHandler<UpdateBookCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public UpdateBookCommandHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<bool> Handle(UpdateBookCommand request, CancellationToken cancellationToken)
    {
        var book = await _context.Books.FindAsync([request.Id], cancellationToken);
        if (book == null) throw new NotFoundException("Book", request.Id);

        book.Update(
            request.Title,
            request.Description,
            request.PageCount,
            request.Subtitle,
            request.ISBN,
            request.PublicationDate,
            request.Language,
            request.Publisher,
            request.CoverImageUrl);

        await _context.SaveChangesAsync(cancellationToken);
        await _cacheService.RemoveAsync($"books:details:{request.Id}", cancellationToken);

        return true;
    }
}

// --- Publish Book ---
public record PublishBookCommand(Guid Id) : IRequest<bool>;

public class PublishBookCommandHandler : IRequestHandler<PublishBookCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public PublishBookCommandHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<bool> Handle(PublishBookCommand request, CancellationToken cancellationToken)
    {
        var book = await _context.Books
            .Include(b => b.Authors)
            .Include(b => b.Genres)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (book == null) throw new NotFoundException("Book", request.Id);

        book.Publish();
        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.RemoveAsync($"books:details:{request.Id}", cancellationToken);
        await _cacheService.RemoveAsync("books:trending", cancellationToken);

        return true;
    }
}

// --- Add Book Edition ---
public record AddBookEditionCommand(
    Guid BookId,
    string ISBN,
    BookEditionFormat Format,
    string? Publisher = null,
    DateOnly? PublicationDate = null,
    int? PageCount = null,
    string? Language = null,
    long? FileSizeInBytes = null,
    string? FileUrl = null) : IRequest<Guid>;

public class AddBookEditionCommandValidator : AbstractValidator<AddBookEditionCommand>
{
    public AddBookEditionCommandValidator()
    {
        RuleFor(x => x.BookId).NotEmpty();
        RuleFor(x => x.ISBN).NotEmpty().IsValidIsbn();
        RuleFor(x => x.Format).IsInEnum();
        RuleFor(x => x.Publisher).MaximumLength(200);
        RuleFor(x => x.PageCount).GreaterThan(0).LessThanOrEqualTo(100000).When(x => x.PageCount.HasValue);
        RuleFor(x => x.Language).MaximumLength(10);
        RuleFor(x => x.FileSizeInBytes).GreaterThan(0).When(x => x.FileSizeInBytes.HasValue);
        RuleFor(x => x.FileUrl).IsValidWebUrl();
    }
}

public class AddBookEditionCommandHandler : IRequestHandler<AddBookEditionCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public AddBookEditionCommandHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<Guid> Handle(AddBookEditionCommand request, CancellationToken cancellationToken)
    {
        var book = await _context.Books
            .Include(b => b.Editions)
            .FirstOrDefaultAsync(b => b.Id == request.BookId, cancellationToken);

        if (book == null) throw new NotFoundException("Book", request.BookId);

        var edition = book.AddEdition(
            request.ISBN,
            request.Format,
            request.Publisher,
            request.PublicationDate,
            request.PageCount,
            request.Language,
            request.FileSizeInBytes,
            request.FileUrl);

        // The edition is discovered with its Guid key already set, so DetectChanges
        // would track it as Modified and emit an UPDATE for a row that does not exist.
        _context.BookEditions.Add(edition);

        await _context.SaveChangesAsync(cancellationToken);
        await _cacheService.RemoveAsync($"books:details:{request.BookId}", cancellationToken);

        return edition.Id;
    }
}
