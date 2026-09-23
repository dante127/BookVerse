using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Books;

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

        var books = await query
            .Include(b => b.Authors).ThenInclude(ba => ba.Author)
            .Include(b => b.Genres).ThenInclude(bg => bg.Genre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = books.Select(BookMapper.ToSummaryDto).ToList();

        return new PagedResult<BookSummaryDto>(items, totalCount, page, pageSize);
    }
}
