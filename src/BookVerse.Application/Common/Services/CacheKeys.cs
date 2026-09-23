namespace BookVerse.Application.Common.Services;

/// <summary>
/// CQ-03: every cache key literal lived as an ad-hoc string in whichever handler
/// touched it; invalidation sites and producer sites now share these constants.
/// </summary>
public static class CacheKeys
{
    public const string TrendingBooks = "books:trending";
    public const string GenresTaxonomy = "genres:taxonomy";
    public const string AdminDashboard = "analytics:admin:dashboard";

    public static string BookDetails(Guid bookId) => $"books:details:{bookId}";

    public static string UserRecommendations(Guid userId) => $"recs:user:{userId}";
}

public static class CacheTtls
{
    public static readonly TimeSpan BookDetails = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TrendingBooks = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GenresTaxonomy = TimeSpan.FromHours(1);
    public static readonly TimeSpan AdminDashboard = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan UserRecommendations = TimeSpan.FromMinutes(15);
}
