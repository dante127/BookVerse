using BookVerse.Application.Common.Interfaces;
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
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var recommendationService = scope.ServiceProvider.GetRequiredService<IRecommendationService>();
                var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

                // Invalidate existing trending cache and compute fresh scores
                await cacheService.RemoveAsync("books:trending", stoppingToken);
                var trending = await recommendationService.GetTrendingBooksAsync(20, stoppingToken);

                _logger.LogInformation("Successfully refreshed trending books cache ({Count} books).", trending.Count);
            }
            catch (Exception ex)
            {
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
            }
            catch (Exception ex)
            {
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
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var books = await context.Books
                    .Where(b => b.RatingsCount > 0)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                var modified = false;

                foreach (var book in books)
                {
                    var approvedReviews = await context.BookReviews
                        .Where(r => r.BookId == book.Id && r.Status == Domain.Enums.ReviewStatus.Published)
                        .ToListAsync(stoppingToken);

                    if (approvedReviews.Count != 0)
                    {
                        var actualCount = approvedReviews.Count;
                        var actualAvg = Math.Round((decimal)approvedReviews.Average(r => r.Rating), 2);

                        if (book.RatingsCount != actualCount || Math.Abs(book.AverageRating - actualAvg) > 0.05m)
                        {
                            _logger.LogWarning("Healed rating drift on Book {BookId}: ({OldAvg} -> {NewAvg})", book.Id, book.AverageRating, actualAvg);
                            // Heal drift
                            book.UpdateExistingRating((int)book.AverageRating, (int)actualAvg);
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during aggregate rating reconciliation.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }
}
