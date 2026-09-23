using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Common.Validation;
using BookVerse.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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
        await _cacheService.RemoveAsync(CacheKeys.BookDetails(request.BookId), cancellationToken);

        return edition.Id;
    }
}
