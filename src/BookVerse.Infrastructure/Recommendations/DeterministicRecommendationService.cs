using BookVerse.Application.Common.Interfaces;
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
        var cacheKey = $"recs:user:{userId}";
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
            // S1: Genre Affinity (0.30)
            var genreOverlap = likedGenreIds.Count != 0
                ? (double)b.Genres.Count(g => likedGenreIds.Contains(g.GenreId)) / Math.Max(1, b.Genres.Count)
                : 0.0;
            var sGenre = Math.Min(1.0, genreOverlap);

            // S2: Author Affinity (0.25)
            var sAuthor = 0.0;
            if (b.Authors.Any(a => followedAuthorIds.Contains(a.AuthorId))) sAuthor = 1.0;
            else if (b.Authors.Any(a => preferredAuthorIds.Contains(a.AuthorId))) sAuthor = 0.7;

            // S3: Tag Similarity (0.20)
            var sTag = Math.Min(1.0, b.Tags.Count * 0.2);

            // S4: Normalized Rating (0.15)
            var sRating = (double)b.AverageRating / 5.0;

            // S5: Popularity Factor (0.05)
            var sPop = Math.Min(1.0, Math.Log10(b.RatingsCount + 1) / 4.0);

            // S6: Recency Decay (0.05)
            var deltaYears = b.PublicationDate.HasValue ? Math.Max(0, currentYear - b.PublicationDate.Value.Year) : 10;
            var sRec = Math.Exp(-0.05 * deltaYears);

            // Total Score
            var totalScore = (0.30 * sGenre) +
                             (0.25 * sAuthor) +
                             (0.20 * sTag) +
                             (0.15 * sRating) +
                             (0.05 * sPop) +
                             (0.05 * sRec);

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

        await _cacheService.SetAsync(cacheKey, results, TimeSpan.FromMinutes(15), cancellationToken);

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

            // Genre Jaccard overlap
            var genreUnion = targetGenres.Union(cGenres).Count();
            var genreOverlap = genreUnion > 0 ? (double)targetGenres.Intersect(cGenres).Count() / genreUnion : 0.0;

            // Tag Jaccard overlap
            var tagUnion = targetTags.Union(cTags).Count();
            var tagOverlap = tagUnion > 0 ? (double)targetTags.Intersect(cTags).Count() / tagUnion : 0.0;

            // Author match
            var authorMatch = targetAuthors.Overlaps(cAuthors) ? 1.0 : 0.0;

            // Rating proximity
            var ratingDiff = Math.Abs((double)(targetBook.AverageRating - candidate.AverageRating));
            var ratingProximity = Math.Max(0.0, 1.0 - (ratingDiff / 5.0));

            var score = (0.45 * genreOverlap) + (0.35 * tagOverlap) + (0.10 * authorMatch) + (0.10 * ratingProximity);

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
        const string cacheKey = "books:trending";
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

            var trendingScore = (reads * 3.0) + (reviews * 2.5) + (favs * 2.0) + ((double)b.AverageRating * 1.5) + Math.Log10(b.RatingsCount + 1);

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

        await _cacheService.SetAsync(cacheKey, trendingList, TimeSpan.FromMinutes(5), cancellationToken);

        return trendingList;
    }
}
