using System.Diagnostics;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookVerse.Infrastructure.BackgroundJobs;

public class TrendingRecalculationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrendingRecalculationWorker> _logger;

    public TrendingRecalculationWorker(IServiceProvider serviceProvider, ILogger<TrendingRecalculationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Trending Recalculation Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var recommendationService = scope.ServiceProvider.GetRequiredService<IRecommendationService>();
                var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

                // Invalidate existing trending cache and compute fresh scores
                await cacheService.RemoveAsync(CacheKeys.TrendingBooks, stoppingToken);
                var trending = await recommendationService.GetTrendingBooksAsync(20, stoppingToken);

                _logger.LogInformation("Successfully refreshed trending books cache ({Count} books).", trending.Count);
                BookVerseMetrics.RecordWorkerIteration(nameof(TrendingRecalculationWorker), BookVerseMetrics.ElapsedMs(startedAt), success: true);
            }
            catch (Exception ex)
            {
                BookVerseMetrics.RecordWorkerIteration(nameof(TrendingRecalculationWorker), BookVerseMetrics.ElapsedMs(startedAt), success: false);
                _logger.LogError(ex, "Error occurred during trending books recalculation.");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}

public class TokenCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenCleanupWorker> _logger;

    public TokenCleanupWorker(IServiceProvider serviceProvider, ILogger<TokenCleanupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token Cleanup Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddDays(-14);

                var expiredTokens = await context.RefreshTokens
                    .Where(t => (t.RevokedAt != null && t.RevokedAt < cutoff) || t.ExpiresAt < cutoff)
                    .ToListAsync(stoppingToken);

                if (expiredTokens.Count != 0)
                {
                    context.RefreshTokens.RemoveRange(expiredTokens);
                    await context.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Purged {Count} expired and obsolete refresh tokens.", expiredTokens.Count);
                }

                BookVerseMetrics.RecordWorkerIteration(nameof(TokenCleanupWorker), BookVerseMetrics.ElapsedMs(startedAt), success: true);
            }
            catch (Exception ex)
            {
                BookVerseMetrics.RecordWorkerIteration(nameof(TokenCleanupWorker), BookVerseMetrics.ElapsedMs(startedAt), success: false);
                _logger.LogError(ex, "Error occurred during refresh token cleanup.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}

public class AggregateReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AggregateReconciliationWorker> _logger;

    public AggregateReconciliationWorker(IServiceProvider serviceProvider, ILogger<AggregateReconciliationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Aggregate Reconciliation Worker started.");

        // Initial delay so app startup is unaffected
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var bookIds = await context.Books
                    .AsNoTracking()
                    .Where(b => b.RatingsCount > 0)
                    .OrderBy(b => b.Id)
                    .Take(50)
                    .Select(b => b.Id)
                    .ToListAsync(stoppingToken);

                if (bookIds.Count != 0)
                {
                    // One grouped aggregate query instead of loading every review row per book.
                    var stats = await context.BookReviews
                        .AsNoTracking()
                        .Where(r => r.Status == Domain.Enums.ReviewStatus.Published && bookIds.Contains(r.BookId))
                        .GroupBy(r => r.BookId)
                        .Select(g => new { BookId = g.Key, Count = g.Count(), Avg = g.Average(r => r.Rating) })
                        .ToListAsync(stoppingToken);

                    var statsById = stats.ToDictionary(s => s.BookId);

                    var books = await context.Books
                        .Where(b => bookIds.Contains(b.Id))
                        .ToListAsync(stoppingToken);

                    var modified = false;

                    foreach (var book in books)
                    {
                        if (statsById.TryGetValue(book.Id, out var s))
                        {
                            var actualAvg = Math.Round((decimal)s.Avg, 2);
                            if (book.RatingsCount != s.Count || Math.Abs(book.AverageRating - actualAvg) > 0.05m)
                            {
                                _logger.LogWarning("Healed rating drift on Book {BookId}: ({OldAvg}/{OldCount} -> {NewAvg}/{NewCount})",
                                    book.Id, book.AverageRating, book.RatingsCount, actualAvg, s.Count);
                                book.RecalculateRatingAggregates(actualAvg, s.Count);
                                modified = true;
                            }
                        }
                        else if (book.RatingsCount != 0 || book.AverageRating != 0)
                        {
                            _logger.LogWarning("Healed rating drift on Book {BookId}: aggregate points at {Count} reviews but none are published",
                                book.Id, book.RatingsCount);
                            book.RecalculateRatingAggregates(0m, 0);
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }

                BookVerseMetrics.RecordWorkerIteration(nameof(AggregateReconciliationWorker), BookVerseMetrics.ElapsedMs(startedAt), success: true);
            }
            catch (Exception ex)
            {
                BookVerseMetrics.RecordWorkerIteration(nameof(AggregateReconciliationWorker), BookVerseMetrics.ElapsedMs(startedAt), success: false);
                _logger.LogError(ex, "Error occurred during aggregate rating reconciliation.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
