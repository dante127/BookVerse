namespace BookVerse.Infrastructure.Recommendations;

/// <summary>
/// TST-01: single owner for the recommendation formulas. The service computes the
/// signals from data; all weighting math lives here so unit tests exercise the
/// same code that runs in production.
/// </summary>
public static class RecommendationScoring
{
    // Personalized recommendation signal weights (sum = 1.0)
    public const double GenreWeight = 0.30;
    public const double AuthorWeight = 0.25;
    public const double TagWeight = 0.20;
    public const double RatingWeight = 0.15;
    public const double PopularityWeight = 0.05;
    public const double RecencyWeight = 0.05;

    // Similarity weights
    public const double SimilarGenreWeight = 0.45;
    public const double SimilarTagWeight = 0.35;
    public const double SimilarAuthorWeight = 0.10;
    public const double SimilarRatingWeight = 0.10;

    // Trending component weights
    public const double TrendingReadsWeight = 3.0;
    public const double TrendingReviewsWeight = 2.5;
    public const double TrendingFavoritesWeight = 2.0;
    public const double TrendingRatingWeight = 1.5;

    public const double RecencyDecayRate = 0.05;
    public const int MissingPublicationYearAge = 10;

    public static double GenreAffinity(double likedGenreOverlapCount, int bookGenreCount)
        => Math.Min(1.0, (double)likedGenreOverlapCount / Math.Max(1, bookGenreCount));

    public static double AuthorAffinity(bool followed, bool preferred)
        => followed ? 1.0 : (preferred ? 0.7 : 0.0);

    public static double TagSignal(int tagCount)
        => Math.Min(1.0, tagCount * 0.2);

    public static double RatingSignal(decimal averageRating)
        => (double)averageRating / 5.0;

    public static double PopularitySignal(int ratingsCount)
        => Math.Min(1.0, Math.Log10(ratingsCount + 1) / 4.0);

    public static double RecencySignal(DateOnly? publicationDate, int currentYear)
    {
        var deltaYears = publicationDate.HasValue
            ? Math.Max(0, currentYear - publicationDate.Value.Year)
            : MissingPublicationYearAge;
        return Math.Exp(-RecencyDecayRate * deltaYears);
    }

    public static double PersonalizedScore(
        double genre,
        double author,
        double tag,
        double rating,
        double popularity,
        double recency)
        => (GenreWeight * genre) +
           (AuthorWeight * author) +
           (TagWeight * tag) +
           (RatingWeight * rating) +
           (PopularityWeight * popularity) +
           (RecencyWeight * recency);

    public static double JaccardOverlap<T>(IReadOnlyCollection<T> a, IReadOnlyCollection<T> b)
    {
        var union = a.Union(b).Count();
        return union > 0 ? (double)a.Intersect(b).Count() / union : 0.0;
    }

    public static double RatingProximity(decimal a, decimal b)
        => Math.Max(0.0, 1.0 - (Math.Abs((double)(a - b)) / 5.0));

    public static double Similarity(
        double genreOverlap,
        double tagOverlap,
        double authorMatch,
        double ratingProximity)
        => (SimilarGenreWeight * genreOverlap) +
           (SimilarTagWeight * tagOverlap) +
           (SimilarAuthorWeight * authorMatch) +
           (SimilarRatingWeight * ratingProximity);

    public static double TrendingScore(
        int reads,
        int reviews,
        int favorites,
        decimal averageRating,
        int ratingsCount)
        => (reads * TrendingReadsWeight) +
           (reviews * TrendingReviewsWeight) +
           (favorites * TrendingFavoritesWeight) +
           ((double)averageRating * TrendingRatingWeight) +
           Math.Log10(ratingsCount + 1);
}
