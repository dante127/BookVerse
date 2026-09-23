using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Services;
using BookVerse.Application.Common.Extensions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Features.Analytics;

public record UserReadingAnalyticsDto(
    int TotalBooksCompleted,
    int TotalBooksReading,
    int TotalBooksWantToRead,
    int TotalPagesRead,
    decimal AverageRatingGiven,
    int ReadingStreakDays,
    IReadOnlyList<GenreDistributionDto> FavoriteGenres,
    IReadOnlyList<MonthlyReadingDto> MonthlyActivity,
    ReadingGoalStatusDto? CurrentGoal);

public record GenreDistributionDto(string GenreName, int BookCount);
public record MonthlyReadingDto(string Month, int BooksCompleted, int PagesRead);
public record ReadingGoalStatusDto(int Year, int TargetBooks, int CompletedBooks, decimal PercentageCompleted);

public record AdminAnalyticsDto(
    int TotalUsers,
    int ActiveUsers,
    int TotalBooks,
    int PublishedBooks,
    int TotalReviews,
    decimal PlatformAverageRating,
    int TotalPagesReadPlatformWide,
    IReadOnlyList<GenreDistributionDto> TopGenres);

// --- User Reading Analytics ---
public record GetUserReadingAnalyticsQuery : IRequest<UserReadingAnalyticsDto>;

public class GetUserReadingAnalyticsQueryHandler : IRequestHandler<GetUserReadingAnalyticsQuery, UserReadingAnalyticsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetUserReadingAnalyticsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<UserReadingAnalyticsDto> Handle(GetUserReadingAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.RequireUserId();

        var userBooks = await _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .ToListAsync(cancellationToken);

        var completedCount = userBooks.Count(ub => ub.Status == UserBookStatus.Completed);
        var readingCount = userBooks.Count(ub => ub.Status == UserBookStatus.Reading);
        var wantToReadCount = userBooks.Count(ub => ub.Status == UserBookStatus.WantToRead);

        var progressList = await _context.ReadingProgresses
            .AsNoTracking()
            .Where(rp => rp.UserId == userId)
            .ToListAsync(cancellationToken);

        var totalPagesRead = progressList.Sum(rp => rp.CurrentPage);

        var reviews = await _context.BookReviews
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);

        var avgRating = reviews.Count != 0 ? Math.Round((decimal)reviews.Average(r => r.Rating), 2) : 0m;

        // Calculate favorite genres from user's completed or reading books
        var userBookIds = userBooks.Select(ub => ub.BookId).ToList();

        var topGenres = await _context.BookGenres
            .AsNoTracking()
            .Where(bg => userBookIds.Contains(bg.BookId))
            .GroupBy(bg => bg.Genre.Name)
            .Select(g => new GenreDistributionDto(g.Key, g.Count()))
            .OrderByDescending(g => g.BookCount)
            .Take(5)
            .ToListAsync(cancellationToken);

        // Monthly reading activity (last 6 months)
        var sixMonthsAgo = DateTimeOffset.UtcNow.AddMonths(-6);
        var histories = await _context.ReadingHistories
            .AsNoTracking()
            .Where(rh => rh.UserId == userId && rh.Timestamp >= sixMonthsAgo)
            .ToListAsync(cancellationToken);

        var monthlyActivity = histories
            .GroupBy(h => h.Timestamp.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g => new MonthlyReadingDto(
                g.Key,
                g.Count(h => h.Action == ReadingHistoryAction.CompletedBook),
                g.Sum(h => h.DeltaPages)))
            .ToList();

        // Current Year Goal
        var currentYear = DateTime.UtcNow.Year;
        var goal = await _context.ReadingGoals
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId && g.Year == currentYear, cancellationToken);

        var goalDto = goal != null
            ? new ReadingGoalStatusDto(
                goal.Year,
                goal.TargetBooks,
                goal.CompletedBooks,
                goal.TargetBooks > 0 ? Math.Round(((decimal)goal.CompletedBooks / goal.TargetBooks) * 100, 2) : 0)
            : null;

        // Reading streak (simplified calculation based on distinct days with history in last 30 days)
        var recentDays = histories
            .Select(h => h.Timestamp.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        var streak = 0;
        var checkDate = DateTime.UtcNow.Date;
        foreach (var day in recentDays)
        {
            if (day == checkDate || day == checkDate.AddDays(-1))
            {
                streak++;
                checkDate = day;
            }
            else
            {
                break;
            }
        }

        return new UserReadingAnalyticsDto(
            completedCount,
            readingCount,
            wantToReadCount,
            totalPagesRead,
            avgRating,
            streak,
            topGenres,
            monthlyActivity,
            goalDto);
    }
}

// --- Admin Analytics ---
public record GetAdminAnalyticsQuery : IRequest<AdminAnalyticsDto>;

public class GetAdminAnalyticsQueryHandler : IRequestHandler<GetAdminAnalyticsQuery, AdminAnalyticsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public GetAdminAnalyticsQueryHandler(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<AdminAnalyticsDto> Handle(GetAdminAnalyticsQuery request, CancellationToken cancellationToken)
    {
        const string cacheKey = CacheKeys.AdminDashboard;
        var cached = await _cacheService.GetAsync<AdminAnalyticsDto>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var totalUsers = await _context.Users.CountAsync(cancellationToken);
        var activeUsers = await _context.Users.CountAsync(u => u.Status == UserStatus.Active, cancellationToken);

        var totalBooks = await _context.Books.CountAsync(cancellationToken);
        var publishedBooks = await _context.Books.CountAsync(b => b.Status == BookStatus.Published, cancellationToken);

        var totalReviews = await _context.BookReviews.CountAsync(r => r.Status == ReviewStatus.Published, cancellationToken);

        var avgRating = await _context.Books
            .Where(b => b.Status == BookStatus.Published && b.RatingsCount > 0)
            .Select(b => (decimal?)b.AverageRating)
            .AverageAsync(cancellationToken) ?? 0m;

        var totalPages = await _context.ReadingProgresses
            .SumAsync(rp => (long)rp.CurrentPage, cancellationToken);

        var topGenres = await _context.BookGenres
            .GroupBy(bg => bg.Genre.Name)
            .Select(g => new GenreDistributionDto(g.Key, g.Count()))
            .OrderByDescending(g => g.BookCount)
            .Take(8)
            .ToListAsync(cancellationToken);

        var dto = new AdminAnalyticsDto(
            totalUsers,
            activeUsers,
            totalBooks,
            publishedBooks,
            totalReviews,
            Math.Round(avgRating, 2),
            (int)Math.Min(int.MaxValue, totalPages),
            topGenres);

        await _cacheService.SetAsync(cacheKey, dto, CacheTtls.AdminDashboard, cancellationToken);

        return dto;
    }
}
