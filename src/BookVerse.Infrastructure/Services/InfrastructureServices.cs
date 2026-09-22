using BookVerse.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookVerse.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public class ReviewModerationService : IReviewModerationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ReviewModerationService> _logger;

    private static readonly HashSet<string> FlaggedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "buy now", "free crypto", "click here", "viagra", "casino", "scam", "telegram @", "whatsapp +"
    };

    public ReviewModerationService(IApplicationDbContext context, ILogger<ReviewModerationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ReviewModerationResult> EvaluateReviewAsync(
        Guid userId,
        int rating,
        string? title,
        string? content,
        CancellationToken cancellationToken = default)
    {
        if (rating < 1 || rating > 5)
        {
            return new ReviewModerationResult(false, true, "Invalid rating value.");
        }

        // Anti-Abuse 1: Rate Limiting submission velocity
        var tenMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-10);
        var recentSubmissionsCount = await _context.BookReviews
            .CountAsync(r => r.UserId == userId && r.CreatedAt >= tenMinutesAgo, cancellationToken);

        if (recentSubmissionsCount >= 5)
        {
            _logger.LogWarning("Rate limit triggered for user {UserId}: {Count} reviews in 10 mins.", userId, recentSubmissionsCount);
            return new ReviewModerationResult(false, true, "Submission rate limit exceeded. Review held for manual moderation.");
        }

        // Anti-Abuse 2: Keyword Spam Detection
        var fullText = $"{title} {content}";
        foreach (var keyword in FlaggedKeywords)
        {
            if (fullText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Spam keyword detected in review by user {UserId}: '{Keyword}'", userId, keyword);
                return new ReviewModerationResult(false, true, "Content flagged by automated filter for moderator review.");
            }
        }

        // Auto-approved
        return new ReviewModerationResult(true, false, null);
    }
}
