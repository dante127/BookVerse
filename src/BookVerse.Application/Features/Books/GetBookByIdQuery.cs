using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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
        var cacheKey = CacheKeys.BookDetails(request.Id);
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

        var dto = BookMapper.ToDetailDto(book);

        await _cacheService.SetAsync(cacheKey, dto, CacheTtls.BookDetails, cancellationToken);

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
