using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Library;

public record UserBookItemDto(
    Guid Id,
    Guid BookId,
    string Title,
    string? CoverImageUrl,
    UserBookStatus Status,
    DateTimeOffset AddedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastReadAt,
    int? CurrentPage,
    int? TotalPages,
    decimal? Percentage,
    bool IsFavorite);

// --- Get User Library ---
public record GetUserLibraryQuery(
    UserBookStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<UserBookItemDto>>;

public class GetUserLibraryQueryHandler : IRequestHandler<GetUserLibraryQuery, PagedResult<UserBookItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetUserLibraryQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<PagedResult<UserBookItemDto>> Handle(GetUserLibraryQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;
        var (page, pageSize) = Pagination.Normalize(request.Page, request.PageSize);

        var query = _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId);

        if (request.Status.HasValue)
        {
            query = query.Where(ub => ub.Status == request.Status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var favorites = await _context.FavoriteBooks
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.BookId)
            .ToListAsync(cancellationToken);

        var progressList = await _context.ReadingProgresses
            .AsNoTracking()
            .Where(rp => rp.UserId == userId)
            .ToListAsync(cancellationToken);

        var items = await query
            .OrderByDescending(ub => ub.LastReadAt ?? ub.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ub => new
            {
                ub.Id,
                ub.BookId,
                ub.Book.Title,
                ub.Book.CoverImageUrl,
                ub.Status,
                ub.AddedAt,
                ub.StartedAt,
                ub.CompletedAt,
                ub.LastReadAt
            })
            .ToListAsync(cancellationToken);

        var dtos = items.Select(item =>
        {
            var prog = progressList.FirstOrDefault(p => p.BookId == item.BookId);
            return new UserBookItemDto(
                item.Id,
                item.BookId,
                item.Title,
                item.CoverImageUrl,
                item.Status,
                item.AddedAt,
                item.StartedAt,
                item.CompletedAt,
                item.LastReadAt,
                prog?.CurrentPage,
                prog?.TotalPages,
                prog?.Percentage,
                favorites.Contains(item.BookId));
        }).ToList();

        return new PagedResult<UserBookItemDto>(dtos, totalCount, page, pageSize);
    }
}

// --- Add Book to Library ---
public record AddBookToLibraryCommand(Guid BookId, UserBookStatus InitialStatus = UserBookStatus.WantToRead) : IRequest<Guid>;

public class AddBookToLibraryCommandHandler : IRequestHandler<AddBookToLibraryCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddBookToLibraryCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Guid> Handle(AddBookToLibraryCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var book = await _context.Books.FindAsync([request.BookId], cancellationToken);
        if (book == null) throw new NotFoundException("Book", request.BookId);

        var existing = await _context.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);

        if (existing != null)
        {
            existing.TransitionStatus(request.InitialStatus);
            await _context.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var userBook = UserBook.Create(userId, request.BookId, request.InitialStatus);
        _context.UserBooks.Add(userBook);

        // Ensure reading progress record exists
        var progress = await _context.ReadingProgresses
            .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == request.BookId, cancellationToken);

        if (progress == null)
        {
            var initialPage = request.InitialStatus == UserBookStatus.Completed ? book.PageCount : 0;
            _context.ReadingProgresses.Add(ReadingProgress.Create(userId, request.BookId, book.PageCount, initialPage));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return userBook.Id;
    }
}

// --- Update Book Status in Library ---
public record UpdateUserBookStatusCommand(Guid BookId, UserBookStatus NewStatus) : IRequest<bool>;

public class UpdateUserBookStatusCommandHandler : IRequestHandler<UpdateUserBookStatusCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateUserBookStatusCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(UpdateUserBookStatusCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var userBook = await _context.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);

        if (userBook == null)
        {
            throw new NotFoundException($"Book '{request.BookId}' is not in user's library.");
        }

        userBook.TransitionStatus(request.NewStatus);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

// --- Remove Book from Library ---
public record RemoveBookFromLibraryCommand(Guid BookId) : IRequest<bool>;

public class RemoveBookFromLibraryCommandHandler : IRequestHandler<RemoveBookFromLibraryCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RemoveBookFromLibraryCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(RemoveBookFromLibraryCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var userBook = await _context.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);

        if (userBook == null) return false;

        _context.UserBooks.Remove(userBook);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

// --- Favorite & Unfavorite ---
public record FavoriteBookCommand(Guid BookId) : IRequest<bool>;

public class FavoriteBookCommandHandler : IRequestHandler<FavoriteBookCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public FavoriteBookCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(FavoriteBookCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var exists = await _context.FavoriteBooks
            .AnyAsync(f => f.UserId == userId && f.BookId == request.BookId, cancellationToken);

        if (exists) return true;

        _context.FavoriteBooks.Add(new FavoriteBook(userId, request.BookId));
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public record UnfavoriteBookCommand(Guid BookId) : IRequest<bool>;

public class UnfavoriteBookCommandHandler : IRequestHandler<UnfavoriteBookCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UnfavoriteBookCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(UnfavoriteBookCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var favorite = await _context.FavoriteBooks
            .FirstOrDefaultAsync(f => f.UserId == userId && f.BookId == request.BookId, cancellationToken);

        if (favorite == null) return false;

        _context.FavoriteBooks.Remove(favorite);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
