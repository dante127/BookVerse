using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using BookVerse.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Infrastructure.Recommendations;

public class DeterministicRecommendationService : IRecommendationService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public DeterministicRecommendationService(IApplicationDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> GetRecommendationsForUserAsync(
        Guid userId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeys.UserRecommendations(userId);
        var cached = await _cacheService.GetAsync<List<RecommendedBookDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        // 1. User's existing library books to exclude
        var userBooks = await _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .ToListAsync(cancellationToken);

        var excludedBookIds = userBooks
            .Where(ub => ub.Status == UserBookStatus.Reading || ub.Status == UserBookStatus.Completed || ub.Status == UserBookStatus.Dropped)
            .Select(ub => ub.BookId)
            .ToHashSet();

        // 2. Extract User signals
        var followedAuthorIds = await _context.AuthorFollowers
            .AsNoTracking()
            .Where(af => af.UserId == userId)
            .Select(af => af.AuthorId)
            .ToHashSetAsync(cancellationToken);

        var highRatedBookIds = await _context.BookReviews
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Rating >= 4)
            .Select(r => r.BookId)
            .ToListAsync(cancellationToken);

        var preferredAuthorIds = await _context.BookAuthors
            .AsNoTracking()
            .Where(ba => highRatedBookIds.Contains(ba.BookId))
            .Select(ba => ba.AuthorId)
            .ToHashSetAsync(cancellationToken);

        var likedGenreIds = await _context.BookGenres
            .AsNoTracking()
            .Where(bg => highRatedBookIds.Contains(bg.BookId))
            .Select(bg => bg.GenreId)
            .ToHashSetAsync(cancellationToken);

        // Include user's explicitly configured profile preferences
        var profile = await _context.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(profile?.FavoriteGenreIdsJson))
        {
            foreach (var gIdStr in profile.FavoriteGenreIdsJson.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Guid.TryParse(gIdStr, out var gId)) likedGenreIds.Add(gId);
            }
        }

        if (!string.IsNullOrWhiteSpace(profile?.PreferredAuthorIdsJson))
        {
            foreach (var aIdStr in profile.PreferredAuthorIdsJson.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Guid.TryParse(aIdStr, out var aId)) preferredAuthorIds.Add(aId);
            }
        }

        // 3. Load Candidate Published Books
        // The Take() window must be ordered, otherwise the database picks an
        // arbitrary subset of books; quality-first keeps the best candidates.
        var candidates = await _context.Books
            .AsNoTracking()
            .Where(b => b.Status == BookStatus.Published && !excludedBookIds.Contains(b.Id))
            .OrderByDescending(b => b.RatingsCount)
            .ThenByDescending(b => b.AverageRating)
            .ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                b.PublicationDate,
                Authors = b.Authors.Select(a => new { a.AuthorId, a.Author.Name }).ToList(),
                Genres = b.Genres.Select(g => new { g.GenreId, g.Genre.Name }).ToList(),
                Tags = b.Tags.Select(t => t.Tag.Name).ToList()
            })
            .Take(150)
            .ToListAsync(cancellationToken);

        var scoredList = new List<RecommendedBookDto>();
        var currentYear = DateTime.UtcNow.Year;

        foreach (var b in candidates)
        {
            // Signal weights live in RecommendationScoring (TST-01)
            var sGenre = likedGenreIds.Count != 0
                ? RecommendationScoring.GenreAffinity(b.Genres.Count(g => likedGenreIds.Contains(g.GenreId)), b.Genres.Count)
                : 0.0;

            var sAuthor = RecommendationScoring.AuthorAffinity(
                b.Authors.Any(a => followedAuthorIds.Contains(a.AuthorId)),
                b.Authors.Any(a => preferredAuthorIds.Contains(a.AuthorId)));

            var sTag = RecommendationScoring.TagSignal(b.Tags.Count);
            var sRating = RecommendationScoring.RatingSignal(b.AverageRating);
            var sPop = RecommendationScoring.PopularitySignal(b.RatingsCount);
            var sRec = RecommendationScoring.RecencySignal(b.PublicationDate, currentYear);

            var totalScore = RecommendationScoring.PersonalizedScore(sGenre, sAuthor, sTag, sRating, sPop, sRec);

            var reason = sAuthor >= 0.7
                ? "Because you follow or highly rated this author"
                : (sGenre > 0.3 ? "Matches your favorite genres" : "Popular highly rated book in the community");

            scoredList.Add(new RecommendedBookDto(
                b.Id,
                b.Title,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                b.Authors.Select(a => a.Name).ToList(),
                b.Genres.Select(g => g.Name).ToList(),
                Math.Round(totalScore, 3),
                reason));
        }

        var results = scoredList
            .OrderByDescending(r => r.RecommendationScore)
            .Take(limit)
            .ToList();

        await _cacheService.SetAsync(cacheKey, results, CacheTtls.UserRecommendations, cancellationToken);

        return results;
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> GetSimilarBooksAsync(
        Guid bookId,
        int limit = 6,
        CancellationToken cancellationToken = default)
    {
        var targetBook = await _context.Books
            .AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new
            {
                b.Id,
                b.AverageRating,
                AuthorIds = b.Authors.Select(a => a.AuthorId).ToList(),
                GenreIds = b.Genres.Select(g => g.GenreId).ToList(),
                TagIds = b.Tags.Select(t => t.TagId).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (targetBook == null) return [];

        var targetGenres = targetBook.GenreIds.ToHashSet();
        var targetTags = targetBook.TagIds.ToHashSet();
        var targetAuthors = targetBook.AuthorIds.ToHashSet();

        var candidates = await _context.Books
            .AsNoTracking()
            .Where(b => b.Id != bookId && b.Status == BookStatus.Published)
            .OrderByDescending(b => b.RatingsCount)
            .ThenByDescending(b => b.AverageRating)
            .ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                Authors = b.Authors.Select(a => new { a.AuthorId, a.Author.Name }).ToList(),
                Genres = b.Genres.Select(g => new { g.GenreId, g.Genre.Name }).ToList(),
                Tags = b.Tags.Select(t => new { t.TagId, t.Tag.Name }).ToList()
            })
            .Take(100)
            .ToListAsync(cancellationToken);

        var similarList = new List<RecommendedBookDto>();

        foreach (var candidate in candidates)
        {
            var cGenres = candidate.Genres.Select(g => g.GenreId).ToHashSet();
            var cTags = candidate.Tags.Select(t => t.TagId).ToHashSet();
            var cAuthors = candidate.Authors.Select(a => a.AuthorId).ToHashSet();

            var genreOverlap = RecommendationScoring.JaccardOverlap(targetGenres, cGenres);
            var tagOverlap = RecommendationScoring.JaccardOverlap(targetTags, cTags);
            var authorMatch = targetAuthors.Overlaps(cAuthors) ? 1.0 : 0.0;
            var ratingProximity = RecommendationScoring.RatingProximity(targetBook.AverageRating, candidate.AverageRating);

            var score = RecommendationScoring.Similarity(genreOverlap, tagOverlap, authorMatch, ratingProximity);

            similarList.Add(new RecommendedBookDto(
                candidate.Id,
                candidate.Title,
                candidate.CoverImageUrl,
                candidate.AverageRating,
                candidate.RatingsCount,
                candidate.Authors.Select(a => a.Name).ToList(),
                candidate.Genres.Select(g => g.Name).ToList(),
                Math.Round(score, 3),
                "Shared genre, themes and author similarity"));
        }

        return similarList
            .OrderByDescending(r => r.RecommendationScore)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> GetTrendingBooksAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        const string cacheKey = CacheKeys.TrendingBooks;
        var cached = await _cacheService.GetAsync<List<RecommendedBookDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);

        var recentReadsList = await _context.UserBooks
            .AsNoTracking()
            .Where(ub => ub.LastReadAt >= sevenDaysAgo)
            .GroupBy(ub => ub.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var recentReads = recentReadsList.ToDictionary(g => g.BookId, g => g.Count);

        var recentReviewsList = await _context.BookReviews
            .AsNoTracking()
            .Where(r => r.CreatedAt >= sevenDaysAgo && r.Status == ReviewStatus.Published)
            .GroupBy(r => r.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var recentReviews = recentReviewsList.ToDictionary(g => g.BookId, g => g.Count);

        var recentFavoritesList = await _context.FavoriteBooks
            .AsNoTracking()
            .Where(f => f.CreatedAt >= sevenDaysAgo)
            .GroupBy(f => f.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var recentFavorites = recentFavoritesList.ToDictionary(g => g.BookId, g => g.Count);

        var books = await _context.Books
            .AsNoTracking()
            .Where(b => b.Status == BookStatus.Published)
            .OrderByDescending(b => b.RatingsCount)
            .ThenByDescending(b => b.AverageRating)
            .ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                Authors = b.Authors.Select(a => a.Author.Name).ToList(),
                Genres = b.Genres.Select(g => g.Genre.Name).ToList()
            })
            .Take(100)
            .ToListAsync(cancellationToken);

        var trendingList = books.Select(b =>
        {
            recentReads.TryGetValue(b.Id, out var reads);
            recentReviews.TryGetValue(b.Id, out var reviews);
            recentFavorites.TryGetValue(b.Id, out var favs);

            var trendingScore = RecommendationScoring.TrendingScore(reads, reviews, favs, b.AverageRating, b.RatingsCount);

            return new RecommendedBookDto(
                b.Id,
                b.Title,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                b.Authors,
                b.Genres,
                Math.Round(trendingScore, 2),
                "High weekly reader engagement and ratings");
        })
        .OrderByDescending(t => t.RecommendationScore)
        .Take(limit)
        .ToList();

        await _cacheService.SetAsync(cacheKey, trendingList, CacheTtls.TrendingBooks, cancellationToken);

        return trendingList;
    }
}
