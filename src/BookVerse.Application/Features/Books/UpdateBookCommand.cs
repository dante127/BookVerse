using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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
        await _cacheService.RemoveAsync(CacheKeys.BookDetails(request.Id), cancellationToken);

        return true;
    }
}
