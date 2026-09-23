using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using MediatR;

namespace BookVerse.Application.Features.Recommendations;

// --- Get Personalized Recommendations ---
public record GetRecommendationsQuery(int Limit = 10) : IRequest<IReadOnlyList<RecommendedBookDto>>;

public class GetRecommendationsQueryHandler : IRequestHandler<GetRecommendationsQuery, IReadOnlyList<RecommendedBookDto>>
{
    private readonly IRecommendationService _recommendationService;
    private readonly ICurrentUserService _currentUserService;

    public GetRecommendationsQueryHandler(IRecommendationService recommendationService, ICurrentUserService currentUserService)
    {
        _recommendationService = recommendationService;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> Handle(GetRecommendationsQuery request, CancellationToken cancellationToken)
    {
        var limit = Pagination.NormalizeLimit(request.Limit, defaultLimit: 10);

        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            // Anonymous fallback to trending books
            return await _recommendationService.GetTrendingBooksAsync(limit, cancellationToken);
        }

        return await _recommendationService.GetRecommendationsForUserAsync(
            _currentUserService.UserId.Value,
            limit,
            cancellationToken);
    }
}

// --- Get Similar Books ---
public record GetSimilarBooksQuery(Guid BookId, int Limit = 6) : IRequest<IReadOnlyList<RecommendedBookDto>>;

public class GetSimilarBooksQueryHandler : IRequestHandler<GetSimilarBooksQuery, IReadOnlyList<RecommendedBookDto>>
{
    private readonly IRecommendationService _recommendationService;

    public GetSimilarBooksQueryHandler(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> Handle(GetSimilarBooksQuery request, CancellationToken cancellationToken)
    {
        var limit = Pagination.NormalizeLimit(request.Limit, defaultLimit: 6);
        return await _recommendationService.GetSimilarBooksAsync(request.BookId, limit, cancellationToken);
    }
}

// --- Get Trending Books ---
public record GetTrendingBooksQuery(int Limit = 10) : IRequest<IReadOnlyList<RecommendedBookDto>>;

public class GetTrendingBooksQueryHandler : IRequestHandler<GetTrendingBooksQuery, IReadOnlyList<RecommendedBookDto>>
{
    private readonly IRecommendationService _recommendationService;

    public GetTrendingBooksQueryHandler(IRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    public async Task<IReadOnlyList<RecommendedBookDto>> Handle(GetTrendingBooksQuery request, CancellationToken cancellationToken)
    {
        var limit = Pagination.NormalizeLimit(request.Limit, defaultLimit: 10);
        return await _recommendationService.GetTrendingBooksAsync(limit, cancellationToken);
    }
}
