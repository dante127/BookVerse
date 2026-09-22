using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Infrastructure.Search;

public class SqlSearchService : ISearchService
{
    private readonly IApplicationDbContext _context;

    public SqlSearchService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<BookSearchResultDto>> SearchBooksAsync(
        SearchBooksFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Books
            .AsNoTracking()
            .Where(b => b.Status == BookStatus.Published);

        // Structured filters
        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            var lang = filter.Language.Trim().ToLowerInvariant();
            query = query.Where(b => b.Language == lang);
        }

        if (filter.MinRating.HasValue)
        {
            query = query.Where(b => b.AverageRating >= filter.MinRating.Value);
        }

        if (filter.YearFrom.HasValue)
        {
            var fromDate = new DateOnly(filter.YearFrom.Value, 1, 1);
            query = query.Where(b => b.PublicationDate >= fromDate);
        }

        if (filter.YearTo.HasValue)
        {
            var toDate = new DateOnly(filter.YearTo.Value, 12, 31);
            query = query.Where(b => b.PublicationDate <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(filter.Genre))
        {
            var genreSlug = filter.Genre.Trim().ToLowerInvariant();
            query = query.Where(b => b.Genres.Any(g => g.Genre.Slug == genreSlug || g.Genre.Name.Contains(filter.Genre)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tagSlug = filter.Tag.Trim().ToLowerInvariant();
            query = query.Where(b => b.Tags.Any(t => t.Tag.Slug == tagSlug || t.Tag.Name.Contains(filter.Tag)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            var authorQuery = filter.Author.Trim();
            query = query.Where(b => b.Authors.Any(a => a.Author.Name.Contains(authorQuery)));
        }

        // Text query
        var q = filter.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(b =>
                b.Title.Contains(q) ||
                (b.Subtitle != null && b.Subtitle.Contains(q)) ||
                b.Description.Contains(q) ||
                b.Authors.Any(a => a.Author.Name.Contains(q)) ||
                b.Genres.Any(g => g.Genre.Name.Contains(q)) ||
                b.Tags.Any(t => t.Tag.Name.Contains(q)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Projected candidate selection
        var projected = query.Select(b => new
        {
            b.Id,
            b.Title,
            b.Subtitle,
            b.Description,
            b.CoverImageUrl,
            b.AverageRating,
            b.RatingsCount,
            b.ReviewsCount,
            b.PublicationDate,
            Authors = b.Authors.OrderBy(a => a.OrderIndex).Select(a => a.Author.Name).ToList(),
            Genres = b.Genres.Select(g => g.Genre.Name).ToList(),
            Tags = b.Tags.Select(t => t.Tag.Name).ToList()
        });

        // Ordering & Relevance
        var pagedItems = await projected
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var searchResults = pagedItems.Select(b =>
        {
            var score = 1.0;

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qLower = q.ToLowerInvariant();
                var titleLower = b.Title.ToLowerInvariant();

                if (titleLower == qLower) score += 100.0;
                else if (titleLower.StartsWith(qLower)) score += 50.0;
                else if (titleLower.Contains(qLower)) score += 30.0;

                if (b.Authors.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 35.0;
                if (b.Genres.Any(g => g.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 25.0;
                if (b.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))) score += 25.0;
                if (b.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) score += 10.0;

                // Quality multiplier
                var qualityMultiplier = 1.0 + 0.1 * Math.Log10(b.RatingsCount + 1) * ((double)b.AverageRating / 5.0);
                score *= qualityMultiplier;
            }
            else
            {
                score = (double)b.AverageRating * Math.Log10(b.RatingsCount + 10);
            }

            return new BookSearchResultDto(
                b.Id,
                b.Title,
                b.Subtitle,
                b.Description,
                b.CoverImageUrl,
                b.AverageRating,
                b.RatingsCount,
                b.ReviewsCount,
                b.PublicationDate,
                b.Authors,
                b.Genres,
                b.Tags,
                Math.Round(score, 2));
        })
        .OrderByDescending(r => r.RelevanceScore)
        .ToList();

        return new PagedResult<BookSearchResultDto>(searchResults, totalCount, filter.Page, filter.PageSize);
    }
}
