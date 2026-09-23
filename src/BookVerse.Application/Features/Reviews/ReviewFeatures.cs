using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Enums;
using BookVerse.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Reviews;

public record BookReviewDto(
    Guid Id,
    Guid BookId,
    Guid UserId,
    string ReviewerName,
    int Rating,
    string? Title,
    string? Content,
    ReviewStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

// --- Get Book Reviews ---
public record GetBookReviewsQuery(
    Guid BookId,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<BookReviewDto>>;

public class GetBookReviewsQueryHandler : IRequestHandler<GetBookReviewsQuery, PagedResult<BookReviewDto>>
{
    private readonly IApplicationDbContext _context;

    public GetBookReviewsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BookReviewDto>> Handle(GetBookReviewsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(request.Page, request.PageSize);

        var query = _context.BookReviews
            .AsNoTracking()
            .Where(r => r.BookId == request.BookId && r.Status == ReviewStatus.Published);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.BookId,
                r.UserId,
                r.Rating,
                r.Title,
                r.Content,
                r.Status,
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var userIds = items.Select(r => r.UserId).Distinct().ToList();
        var profiles = await _context.UserProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);

        var dtos = items.Select(r => new BookReviewDto(
            r.Id,
            r.BookId,
            r.UserId,
            profiles.TryGetValue(r.UserId, out var name) ? name : "Anonymous Reader",
            r.Rating,
            r.Title,
            r.Content,
            r.Status,
            r.CreatedAt,
            r.UpdatedAt)).ToList();

        return new PagedResult<BookReviewDto>(dtos, totalCount, page, pageSize);
    }
}

// --- Create Review ---
public record CreateReviewCommand(
    Guid BookId,
    int Rating,
    string? Title = null,
    string? Content = null) : IRequest<BookReviewDto>;

public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Title).MaximumLength(150);
        RuleFor(x => x.Content).MaximumLength(4000);
    }
}

public class CreateReviewCommandHandler : IRequestHandler<CreateReviewCommand, BookReviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IReviewModerationService _moderationService;
    private readonly ICacheService _cacheService;

    public CreateReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IReviewModerationService moderationService,
        ICacheService cacheService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _moderationService = moderationService;
        _cacheService = cacheService;
    }

    public async Task<BookReviewDto> Handle(CreateReviewCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var userId = _currentUserService.UserId.Value;
        var isStaff = _currentUserService.IsInRole("Admin") || _currentUserService.IsInRole("Moderator");

        var book = await _context.Books.FindAsync([request.BookId], cancellationToken);
        if (book == null || (!isStaff && book.Status != BookStatus.Published))
            throw new NotFoundException("Book", request.BookId);

        // Eligibility (BUS-01): reviews are limited to readers who hold the book in their
        // library. Staff are exempt so moderators can create seed/test content.
        if (!isStaff)
        {
            var inLibrary = await _context.UserBooks
                .AnyAsync(ub => ub.UserId == userId && ub.BookId == request.BookId, cancellationToken);
            if (!inLibrary)
                throw new ForbiddenException("You can only review books that are in your library.");
        }

        var existingReview = await _context.BookReviews
            .FirstOrDefaultAsync(r => r.BookId == request.BookId && r.UserId == userId, cancellationToken);

        // Anti-abuse moderation check
        var moderationResult = await _moderationService.EvaluateReviewAsync(
            userId,
            request.Rating,
            request.Title,
            request.Content,
            cancellationToken);

        BookReview review;

        if (existingReview != null)
        {
            var oldRating = existingReview.Rating;
            var wasPublished = existingReview.Status == ReviewStatus.Published;

            existingReview.Update(request.Rating, request.Title, request.Content);

            if (!isStaff)
            {
                // Re-moderate on every content change (BUS-02); Rejected/Hidden stay frozen.
                existingReview.ApplyAutomatedModerationResult(moderationResult.IsApproved);
            }

            var isPublished = existingReview.Status == ReviewStatus.Published;
            var hasContent = !string.IsNullOrWhiteSpace(existingReview.Content);

            if (wasPublished && !isPublished)
            {
                book.RemoveRating(existingReview.Rating);
                if (hasContent) book.DecrementReviewCount();
            }
            else if (!wasPublished && isPublished)
            {
                book.ApplyNewRating(existingReview.Rating);
                if (hasContent) book.IncrementReviewCount();
            }
            else if (isPublished)
            {
                book.UpdateExistingRating(oldRating, existingReview.Rating);
            }

            review = existingReview;
        }
        else
        {
            var initialStatus = moderationResult.IsApproved || isStaff
                ? ReviewStatus.Published
                : ReviewStatus.Pending;

            review = BookReview.Create(
                request.BookId,
                userId,
                request.Rating,
                request.Title,
                request.Content,
                initialStatus);

            _context.BookReviews.Add(review);

            if (initialStatus == ReviewStatus.Published)
            {
                book.ApplyNewRating(request.Rating);
                if (!string.IsNullOrWhiteSpace(request.Content))
                {
                    book.IncrementReviewCount();
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cache
        await _cacheService.RemoveAsync($"books:details:{request.BookId}", cancellationToken);
        await _cacheService.RemoveAsync("books:trending", cancellationToken);

        var profile = await _context.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        return new BookReviewDto(
            review.Id,
            review.BookId,
            review.UserId,
            profile?.DisplayName ?? "Anonymous Reader",
            review.Rating,
            review.Title,
            review.Content,
            review.Status,
            review.CreatedAt,
            review.UpdatedAt);
    }
}

// --- Moderate Review ---
public record ModerateReviewCommand(
    Guid ReviewId,
    ReviewStatus NewStatus,
    string? ModerationNote = null) : IRequest<bool>;

public class ModerateReviewCommandValidator : AbstractValidator<ModerateReviewCommand>
{
    public ModerateReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.NewStatus)
            .IsInEnum()
            .Must(s => s != ReviewStatus.Pending)
            .WithMessage("Pending is not a moderation outcome; use Published, Rejected, or Hidden.");
        RuleFor(x => x.ModerationNote)
            .NotEmpty()
            .WithMessage("A moderation note is required when rejecting a review.")
            .When(x => x.NewStatus == ReviewStatus.Rejected);
        RuleFor(x => x.ModerationNote).MaximumLength(1000);
    }
}

public class ModerateReviewCommandHandler : IRequestHandler<ModerateReviewCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cacheService;

    public ModerateReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICacheService cacheService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _cacheService = cacheService;
    }

    public async Task<bool> Handle(ModerateReviewCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
            throw new UnauthorizedException();

        var moderatorId = _currentUserService.UserId.Value;

        var review = await _context.BookReviews
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);

        if (review == null) throw new NotFoundException("Review", request.ReviewId);

        var previousStatus = review.Status;

        switch (request.NewStatus)
        {
            case ReviewStatus.Published:
                review.Approve(moderatorId);
                if (previousStatus != ReviewStatus.Published)
                {
                    review.Book.ApplyNewRating(review.Rating);
                    if (!string.IsNullOrWhiteSpace(review.Content))
                    {
                        review.Book.IncrementReviewCount();
                    }
                }
                break;

            case ReviewStatus.Rejected:
                review.Reject(moderatorId, request.ModerationNote ?? "Content violated community guidelines.");
                if (previousStatus == ReviewStatus.Published)
                {
                    review.Book.RemoveRating(review.Rating);
                    if (!string.IsNullOrWhiteSpace(review.Content))
                    {
                        review.Book.DecrementReviewCount();
                    }
                }
                break;

            case ReviewStatus.Hidden:
                review.Hide(moderatorId);
                if (previousStatus == ReviewStatus.Published)
                {
                    review.Book.RemoveRating(review.Rating);
                    if (!string.IsNullOrWhiteSpace(review.Content))
                    {
                        review.Book.DecrementReviewCount();
                    }
                }
                break;
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _cacheService.RemoveAsync($"books:details:{review.BookId}", cancellationToken);
        await _cacheService.RemoveAsync("books:trending", cancellationToken);

        return true;
    }
}
