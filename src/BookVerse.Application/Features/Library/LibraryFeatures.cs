using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Common.Services;
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

// --- Get One Library Entry (backs the Location URI returned on add) ---
public record GetLibraryBookQuery(Guid BookId) : IRequest<UserBookItemDto>;

public class GetLibraryBookQueryHandler : IRequestHandler<GetLibraryBookQuery, UserBookItemDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetLibraryBookQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<UserBookItemDto> Handle(GetLibraryBookQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var item = await _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.BookId == request.BookId)
            .Select(ub => new
            {
                ub.Id,
                ub.BookId,
                Title = ub.Book.Title,
                CoverImageUrl = ub.Book.CoverImageUrl,
                ub.Status,
                ub.AddedAt,
                ub.StartedAt,
                ub.CompletedAt,
                ub.LastReadAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (item == null)
            throw new NotFoundException("Library entry", request.BookId);

        var progress = await _context.ReadingProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == request.BookId, cancellationToken);

        var isFavorite = await _context.FavoriteBooks
            .AsNoTracking()
            .AnyAsync(f => f.UserId == userId && f.BookId == request.BookId, cancellationToken);

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
            progress?.CurrentPage,
            progress?.TotalPages,
            progress?.Percentage,
            isFavorite);
    }
}

// --- Add Book to Library ---
public record AddBookToLibraryCommand(Guid BookId, UserBookStatus InitialStatus = UserBookStatus.WantToRead) : IRequest<AddBookToLibraryResult>;

/// <summary>Created=false when the book was already in the library (re-add is an idempotent status update).</summary>
public record AddBookToLibraryResult(Guid Id, bool Created);

public class AddBookToLibraryCommandHandler : IRequestHandler<AddBookToLibraryCommand, AddBookToLibraryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddBookToLibraryCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<AddBookToLibraryResult> Handle(AddBookToLibraryCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var book = await _context.Books.FindAsync([request.BookId], cancellationToken);
        if (book == null) throw new NotFoundException("Book", request.BookId);

        var userBook = await _context.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);
        var created = userBook == null;

        if (userBook == null)
        {
            userBook = UserBook.Create(userId, request.BookId, request.InitialStatus);
            _context.UserBooks.Add(userBook);

            // A fresh, not-yet-completed entry gets an empty progress record.
            if (request.InitialStatus != UserBookStatus.Completed)
            {
                var progress = await _context.ReadingProgresses
                    .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == request.BookId, cancellationToken);

                if (progress == null)
                {
                    _context.ReadingProgresses.Add(ReadingProgress.Create(userId, request.BookId, book.PageCount, 0));
                }
            }
        }
        else
        {
            userBook.TransitionStatus(request.InitialStatus);
        }

        // BL-04: one owner syncs progress and the yearly goal with the new status.
        await ReadingCompletion.SyncCompletionStatusAsync(
            _context, userId, book, request.InitialStatus == UserBookStatus.Completed, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        return new AddBookToLibraryResult(userBook.Id, created);
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

        var book = await _context.Books.FindAsync([request.BookId], cancellationToken);
        if (book == null) throw new NotFoundException("Book", request.BookId);

        userBook.TransitionStatus(request.NewStatus);

        // BL-04: status changes now keep progress and the yearly goal in sync.
        await ReadingCompletion.SyncCompletionStatusAsync(
            _context, userId, book, request.NewStatus == UserBookStatus.Completed, cancellationToken);

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

        // BL-04: removing a completed book frees its slot in the yearly goal.
        if (userBook.Status == UserBookStatus.Completed)
        {
            await ReadingCompletion.ApplyGoalEdgeAsync(_context, userId, wasCompleted: true, isCompleted: false, cancellationToken);
        }

        // BL-07: delete the per-user book rows so nothing is orphaned.
        _context.UserBooks.Remove(userBook);

        var favorites = await _context.FavoriteBooks
            .Where(f => f.UserId == userId && f.BookId == request.BookId)
            .ToListAsync(cancellationToken);
        _context.FavoriteBooks.RemoveRange(favorites);

        var progresses = await _context.ReadingProgresses
            .Where(rp => rp.UserId == userId && rp.BookId == request.BookId)
            .ToListAsync(cancellationToken);
        _context.ReadingProgresses.RemoveRange(progresses);

        var histories = await _context.ReadingHistories
            .Where(rh => rh.UserId == userId && rh.BookId == request.BookId)
            .ToListAsync(cancellationToken);
        _context.ReadingHistories.RemoveRange(histories);

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

        // API-04: false means "already favorited" so the endpoint can answer 200 vs 201.
        if (exists) return false;

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
