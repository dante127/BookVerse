using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Validation;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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
