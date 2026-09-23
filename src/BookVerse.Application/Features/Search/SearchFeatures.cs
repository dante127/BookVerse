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
        var (page, pageSize) = Pagination.Normalize(request.Filter.Page, request.Filter.PageSize);
        var filter = request.Filter with { Page = page, PageSize = pageSize };
        return await _searchService.SearchBooksAsync(filter, cancellationToken);
    }
}
