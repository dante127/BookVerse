using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using MediatR;

namespace BookVerse.Application.Features.Search;

public record SearchBooksQuery(SearchBooksFilter Filter) : IRequest<PagedResult<BookSearchResultDto>>;

public class SearchBooksQueryHandler : IRequestHandler<SearchBooksQuery, PagedResult<BookSearchResultDto>>
{
    private readonly ISearchService _searchService;

    public SearchBooksQueryHandler(ISearchService searchService)
    {
        _searchService = searchService;
    }

    public async Task<PagedResult<BookSearchResultDto>> Handle(SearchBooksQuery request, CancellationToken cancellationToken)
    {
        return await _searchService.SearchBooksAsync(request.Filter, cancellationToken);
    }
}
