using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Common.Services;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Reading;

public record ReadingProgressResultDto(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    decimal Percentage,
    bool IsCompleted,
    DateTimeOffset LastReadAt);

public record ReadingHistoryItemDto(
    Guid Id,
    Guid BookId,
    string BookTitle,
    string? CoverImageUrl,
    ReadingHistoryAction Action,
    int DeltaPages,
    int PreviousPage,
    int NewPage,
    DateTimeOffset Timestamp);

public record ReadingGoalDto(
    Guid Id,
    int Year,
    int TargetBooks,
    int CompletedBooks,
    decimal PercentageCompleted);

// --- Update Reading Progress ---
public record UpdateReadingProgressCommand(Guid BookId, int CurrentPage) : IRequest<ReadingProgressResultDto>;

public class UpdateReadingProgressCommandValidator : AbstractValidator<UpdateReadingProgressCommand>
{
    public UpdateReadingProgressCommandValidator()
    {
        RuleFor(x => x.CurrentPage).GreaterThanOrEqualTo(0);
    }
}

public class UpdateReadingProgressCommandHandler : IRequestHandler<UpdateReadingProgressCommand, ReadingProgressResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateReadingProgressCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ReadingProgressResultDto> Handle(UpdateReadingProgressCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        var book = await _context.Books.FindAsync([request.BookId], cancellationToken);
        if (book == null) throw new NotFoundException("Book", request.BookId);

        var progress = await _context.ReadingProgresses
            .FirstOrDefaultAsync(rp => rp.UserId == userId && rp.BookId == request.BookId, cancellationToken);

        var previousPage = progress?.CurrentPage ?? 0;
        var wasCompleted = progress?.CompletedAt != null;

        if (progress == null)
        {
            progress = ReadingProgress.Create(userId, request.BookId, book.PageCount, request.CurrentPage);
            _context.ReadingProgresses.Add(progress);
        }
        else
        {
            // BL-05: the book's live page count wins over the stored snapshot.
            progress.ReconcileTotalPages(book.PageCount);
            progress.UpdateProgress(request.CurrentPage);
        }

        var isCompleted = progress.CompletedAt != null;

        // Update UserBook status
        var userBook = await _context.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);

        if (userBook == null)
        {
            userBook = Domain.Entities.Library.UserBook.Create(
                userId,
                request.BookId,
                isCompleted ? UserBookStatus.Completed : UserBookStatus.Reading);
            _context.UserBooks.Add(userBook);
        }
        else
        {
            userBook.RecordReadingActivity();
            if (isCompleted && userBook.Status != UserBookStatus.Completed)
            {
                userBook.TransitionStatus(UserBookStatus.Completed);
            }
            else if (!isCompleted && userBook.Status is UserBookStatus.WantToRead or UserBookStatus.Completed)
            {
                // BL-04: rewinding past the last page un-completes the book.
                userBook.TransitionStatus(UserBookStatus.Reading);
            }
        }

        // Record Reading History
        var historyAction = isCompleted
            ? ReadingHistoryAction.CompletedBook
            : (previousPage == 0 && request.CurrentPage > 0 ? ReadingHistoryAction.StartedBook : ReadingHistoryAction.ProgressUpdated);

        var history = ReadingHistory.Record(userId, request.BookId, historyAction, previousPage, request.CurrentPage);
        _context.ReadingHistories.Add(history);

        // BL-04: goal moves exactly once per completion edge (increment or decrement).
        await ReadingCompletion.ApplyGoalEdgeAsync(_context, userId, wasCompleted, isCompleted, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new ReadingProgressResultDto(
            request.BookId,
            progress.CurrentPage,
            progress.TotalPages,
            progress.Percentage,
            isCompleted,
            progress.LastReadAt);
    }
}

// --- Get Reading History ---
public record GetReadingHistoryQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<ReadingHistoryItemDto>>;

public class GetReadingHistoryQueryHandler : IRequestHandler<GetReadingHistoryQuery, PagedResult<ReadingHistoryItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetReadingHistoryQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<PagedResult<ReadingHistoryItemDto>> Handle(GetReadingHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;
        var (page, pageSize) = Pagination.Normalize(request.Page, request.PageSize);

        var query = _context.ReadingHistories
            .AsNoTracking()
            .Where(rh => rh.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(rh => rh.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(rh => new ReadingHistoryItemDto(
                rh.Id,
                rh.BookId,
                rh.Book.Title,
                rh.Book.CoverImageUrl,
                rh.Action,
                rh.DeltaPages,
                rh.PreviousPage,
                rh.NewPage,
                rh.Timestamp))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReadingHistoryItemDto>(items, totalCount, page, pageSize);
    }
}

// --- Reading Goals ---
public record GetReadingGoalsQuery : IRequest<IReadOnlyList<ReadingGoalDto>>;

public class GetReadingGoalsQueryHandler : IRequestHandler<GetReadingGoalsQuery, IReadOnlyList<ReadingGoalDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetReadingGoalsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<ReadingGoalDto>> Handle(GetReadingGoalsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;

        return await _context.ReadingGoals
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.Year)
            .Select(g => new ReadingGoalDto(
                g.Id,
                g.Year,
                g.TargetBooks,
                g.CompletedBooks,
                g.TargetBooks > 0 ? Math.Round(((decimal)g.CompletedBooks / g.TargetBooks) * 100, 2) : 0))
            .ToListAsync(cancellationToken);
    }
}

public record SetReadingGoalCommand(int Year, int TargetBooks) : IRequest<ReadingGoalDto>;

public class SetReadingGoalCommandHandler : IRequestHandler<SetReadingGoalCommand, ReadingGoalDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetReadingGoalCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<ReadingGoalDto> Handle(SetReadingGoalCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        if (request.TargetBooks <= 0)
            throw new Common.Exceptions.ValidationException("targetBooks", "Target books must be greater than zero.");

        var userId = _currentUserService.UserId.Value;

        var goal = await _context.ReadingGoals
            .FirstOrDefaultAsync(g => g.UserId == userId && g.Year == request.Year, cancellationToken);

        if (goal == null)
        {
            goal = ReadingGoal.Create(userId, request.Year, request.TargetBooks);
            _context.ReadingGoals.Add(goal);
        }
        else
        {
            goal.UpdateTarget(request.TargetBooks);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ReadingGoalDto(
            goal.Id,
            goal.Year,
            goal.TargetBooks,
            goal.CompletedBooks,
            goal.TargetBooks > 0 ? Math.Round(((decimal)goal.CompletedBooks / goal.TargetBooks) * 100, 2) : 0);
    }
}
