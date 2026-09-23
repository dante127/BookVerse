using BookVerse.Infrastructure.Recommendations;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Application;

/// <summary>
/// TST-01: these assert golden values against RecommendationScoring itself,
/// not a copy of the formula. Changing a weight in production code must break these.
/// </summary>
public class RecommendationScoringTests
{
    [Fact]
    public void PersonalizedWeights_SumToOne()
    {
        var sum = RecommendationScoring.GenreWeight + RecommendationScoring.AuthorWeight +
                  RecommendationScoring.TagWeight + RecommendationScoring.RatingWeight +
                  RecommendationScoring.PopularityWeight + RecommendationScoring.RecencyWeight;

        sum.Should().BeApproximately(1.0, 0.0001);
    }

    [Theory]
    [InlineData(2, 3, 0.6667)]  // 2 of 3 genres liked
    [InlineData(5, 3, 1.0)]     // overlap capped at 1.0
    [InlineData(0, 3, 0.0)]
    public void GenreAffinity_PartialMatchAndCap(int likedOverlap, int genreCount, double expected)
    {
        RecommendationScoring.GenreAffinity(likedOverlap, genreCount)
            .Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData(true, true, 1.0)]   // followed wins over preferred
    [InlineData(true, false, 1.0)]
    [InlineData(false, true, 0.7)]
    [InlineData(false, false, 0.0)]
    public void AuthorAffinity_FollowedPreferredNeither(bool followed, bool preferred, double expected)
    {
        RecommendationScoring.AuthorAffinity(followed, preferred).Should().Be(expected);
    }

    [Theory]
    [InlineData(3, 0.6)]
    [InlineData(10, 1.0)] // capped
    public void TagSignal_ScalesWithCountAndCaps(int tagCount, double expected)
    {
        RecommendationScoring.TagSignal(tagCount).Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void PopularitySignal_LogScaled()
    {
        // log10(101) / 4 ≈ 0.5011
        RecommendationScoring.PopularitySignal(100).Should().BeApproximately(0.5011, 0.001);
        RecommendationScoring.PopularitySignal(0).Should().Be(0.0);
        RecommendationScoring.PopularitySignal(10000).Should().Be(1.0); // capped
    }

    [Fact]
    public void RecencySignal_DecaysFivePercentPerYear_AndPenalisesMissingDate()
    {
        var thisYear = 2026;
        RecommendationScoring.RecencySignal(new DateOnly(thisYear, 6, 1), thisYear)
            .Should().BeApproximately(1.0, 0.001);
        RecommendationScoring.RecencySignal(new DateOnly(thisYear - 10, 1, 1), thisYear)
            .Should().BeApproximately(Math.Exp(-0.5), 0.001);
        // Missing publication date is treated as 10 years old
        RecommendationScoring.RecencySignal(null, thisYear)
            .Should().BeApproximately(Math.Exp(-0.05 * RecommendationScoring.MissingPublicationYearAge), 0.001);
    }

    [Fact]
    public void RecommendationScore_FormulaVerification()
    {
        var score = RecommendationScoring.PersonalizedScore(
            genre: 1.0,   // Perfect genre match
            author: 1.0,  // Followed author
            tag: 0.8,     // High tag overlap
            rating: RecommendationScoring.RatingSignal(5.0m),
            popularity: 0.5, // Moderate popularity
            recency: 1.0);   // Released this year

        // 0.30 + 0.25 + 0.16 + 0.15 + 0.025 + 0.05 = 0.935
        score.Should().BeApproximately(0.935, 0.001);
    }

    [Fact]
    public void RecommendationScore_AllSignalsZero_IsZero()
    {
        RecommendationScoring.PersonalizedScore(0, 0, 0, 0, 0, 0).Should().Be(0.0);
    }

    [Fact]
    public void JaccardOverlap_SetSemantics()
    {
        var a = new HashSet<Guid> { Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var b = new HashSet<Guid> { Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("00000000-0000-0000-0000-000000000004") };
        var empty = new HashSet<Guid>();

        RecommendationScoring.JaccardOverlap(a, b).Should().BeApproximately(1.0 / 4.0, 0.001);
        RecommendationScoring.JaccardOverlap(a, a).Should().Be(1.0);
        RecommendationScoring.JaccardOverlap(a, empty).Should().Be(0.0);
    }

    [Fact]
    public void RatingProximity_FloorAtZero()
    {
        RecommendationScoring.RatingProximity(4.5m, 4.2m).Should().BeApproximately(0.94, 0.001);
        RecommendationScoring.RatingProximity(5.0m, 0.0m).Should().Be(0.0);
    }

    [Fact]
    public void SimilarBooks_SimilarityFormulaVerification()
    {
        var similarity = RecommendationScoring.Similarity(
            genreOverlap: 0.60,
            tagOverlap: 0.50,
            authorMatch: 1.0, // Same author
            ratingProximity: RecommendationScoring.RatingProximity(4.5m, 4.2m));

        // 0.27 + 0.175 + 0.10 + 0.094 = 0.639
        similarity.Should().BeApproximately(0.639, 0.001);
    }

    [Fact]
    public void TrendingBooks_ScoreFormulaVerification()
    {
        var score = RecommendationScoring.TrendingScore(
            reads: 10,
            reviews: 4,
            favorites: 6,
            averageRating: 4.8m,
            ratingsCount: 100);

        // 30 + 10 + 12 + 7.2 + 2.004 = 61.204
        score.Should().BeApproximately(61.204, 0.01);
    }

    [Fact]
    public void TrendingBooks_EngagementOutweighsRating()
    {
        var engaged = RecommendationScoring.TrendingScore(reads: 10, reviews: 4, favorites: 6, averageRating: 4.0m, ratingsCount: 100);
        var quiet = RecommendationScoring.TrendingScore(reads: 0, reviews: 0, favorites: 0, averageRating: 5.0m, ratingsCount: 100);

        engaged.Should().BeGreaterThan(quiet);
    }
}
