using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
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
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            // Anonymous fallback to trending books
            return await _recommendationService.GetTrendingBooksAsync(request.Limit, cancellationToken);
        }

        return await _recommendationService.GetRecommendationsForUserAsync(
            _currentUserService.UserId.Value,
            request.Limit,
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
        return await _recommendationService.GetSimilarBooksAsync(request.BookId, request.Limit, cancellationToken);
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
        return await _recommendationService.GetTrendingBooksAsync(request.Limit, cancellationToken);
    }
}
