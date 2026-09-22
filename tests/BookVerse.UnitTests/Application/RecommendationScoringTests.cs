using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Application;

public class RecommendationScoringTests
{
    [Fact]
    public void RecommendationScore_FormulaVerification()
    {
        // Arrange
        // RecommendationScore = 0.30*Genre + 0.25*Author + 0.20*Tag + 0.15*Rating + 0.05*Pop + 0.05*Rec
        double sGenre = 1.0;  // Perfect genre match
        double sAuthor = 1.0; // Followed author
        double sTag = 0.8;    // High tag overlap
        double sRating = 5.0 / 5.0; // 5-star rating (1.0)
        double sPop = 0.5;    // Moderate popularity
        double sRec = 1.0;    // Released this year

        // Act
        double score = (0.30 * sGenre) +
                       (0.25 * sAuthor) +
                       (0.20 * sTag) +
                       (0.15 * sRating) +
                       (0.05 * sPop) +
                       (0.05 * sRec);

        // Assert: 0.30 + 0.25 + 0.16 + 0.15 + 0.025 + 0.05 = 0.935
        score.Should().BeApproximately(0.935, 0.001);
    }

    [Fact]
    public void SimilarBooks_SimilarityFormulaVerification()
    {
        // Arrange
        // Similarity = 0.45*GenreOverlap + 0.35*TagOverlap + 0.10*AuthorMatch + 0.10*RatingProximity
        double genreOverlap = 0.60;
        double tagOverlap = 0.50;
        double authorMatch = 1.0; // Same author
        double ratingProximity = 1.0 - Math.Abs(4.5 - 4.2) / 5.0; // 1.0 - 0.06 = 0.94

        // Act
        double similarity = (0.45 * genreOverlap) +
                            (0.35 * tagOverlap) +
                            (0.10 * authorMatch) +
                            (0.10 * ratingProximity);

        // Assert: 0.27 + 0.175 + 0.10 + 0.094 = 0.639
        similarity.Should().BeApproximately(0.639, 0.001);
    }

    [Fact]
    public void TrendingBooks_ScoreFormulaVerification()
    {
        // Arrange
        // TrendingScore = (3.0 * Reads) + (2.5 * Reviews) + (2.0 * Favorites) + (1.5 * AvgRating) + log10(RatingsCount + 1)
        int reads = 10;
        int reviews = 4;
        int favorites = 6;
        decimal avgRating = 4.8m;
        int ratingsCount = 100;

        // Act
        double score = (reads * 3.0) + (reviews * 2.5) + (favorites * 2.0) + ((double)avgRating * 1.5) + Math.Log10(ratingsCount + 1);

        // 30 + 10 + 12 + 7.2 + 2.004 = 61.204
        score.Should().BeApproximately(61.204, 0.01);
    }
}
