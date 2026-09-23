using BookVerse.Application.Common.Models;
using BookVerse.Domain.Entities.Audit;
using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Notifications;
using BookVerse.Domain.Entities.Profiles;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserSession> UserSessions { get; }

    DbSet<Book> Books { get; }
    DbSet<BookEdition> BookEditions { get; }
    DbSet<Author> Authors { get; }
    DbSet<BookAuthor> BookAuthors { get; }
    DbSet<AuthorFollower> AuthorFollowers { get; }
    DbSet<Genre> Genres { get; }
    DbSet<BookGenre> BookGenres { get; }
    DbSet<Tag> Tags { get; }
    DbSet<BookTag> BookTags { get; }

    DbSet<UserBook> UserBooks { get; }
    DbSet<FavoriteBook> FavoriteBooks { get; }
    DbSet<ReadingProgress> ReadingProgresses { get; }
    DbSet<ReadingHistory> ReadingHistories { get; }
    DbSet<ReadingGoal> ReadingGoals { get; }
    DbSet<BookReview> BookReviews { get; }

    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);
    bool IsInRole(string role);
}

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    (string Hash, string Salt) HashPassword(string password);
    bool VerifyPassword(string password, string storedHash, string storedSalt);
}

public interface ITokenService
{
    string GenerateAccessToken(User user, UserProfile? profile, IEnumerable<string> permissions);
    string GenerateRefreshToken();
    string HashToken(string token);
    TimeSpan RefreshTokenLifetime { get; }
}

public record ReviewModerationResult(bool IsApproved, bool IsFlagged, string? Reason);

public interface IReviewModerationService
{
    Task<ReviewModerationResult> EvaluateReviewAsync(
        Guid userId,
        int rating,
        string? title,
        string? content,
        CancellationToken cancellationToken = default);
}

public record BookSearchResultDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string Description,
    string? CoverImageUrl,
    decimal AverageRating,
    int RatingsCount,
    int ReviewsCount,
    DateOnly? PublicationDate,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Tags,
    double RelevanceScore);

public record SearchBooksFilter(
    string? Query,
    string? Genre,
    string? Tag,
    string? Author,
    decimal? MinRating,
    string? Language,
    int? YearFrom,
    int? YearTo,
    string? SortBy,
    int Page = 1,
    int PageSize = 20);

public interface ISearchService
{
    Task<PagedResult<BookSearchResultDto>> SearchBooksAsync(
        SearchBooksFilter filter,
        CancellationToken cancellationToken = default);
}

public record RecommendedBookDto(
    Guid Id,
    string Title,
    string? CoverImageUrl,
    decimal AverageRating,
    int RatingsCount,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    double RecommendationScore,
    string MatchReason);

public interface IRecommendationService
{
    Task<IReadOnlyList<RecommendedBookDto>> GetRecommendationsForUserAsync(
        Guid userId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendedBookDto>> GetSimilarBooksAsync(
        Guid bookId,
        int limit = 6,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecommendedBookDto>> GetTrendingBooksAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);
}
