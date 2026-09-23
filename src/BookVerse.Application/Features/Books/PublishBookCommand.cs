using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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

        await _cacheService.RemoveAsync(CacheKeys.BookDetails(request.Id), cancellationToken);
        await _cacheService.RemoveAsync(CacheKeys.TrendingBooks, cancellationToken);

        return true;
    }
}
