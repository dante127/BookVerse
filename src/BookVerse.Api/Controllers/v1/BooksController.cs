using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Books;
using BookVerse.Application.Features.Library;
using BookVerse.Application.Features.Reading;
using BookVerse.Application.Features.Recommendations;
using BookVerse.Application.Features.Reviews;
using BookVerse.Application.Features.Search;
using BookVerse.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[Route("api/v1/books")]
public class BooksController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BookSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<BookSummaryDto>>>> GetBooks(
        [FromQuery] Guid? genreId,
        [FromQuery] BookStatus? status = BookStatus.Published,
        [FromQuery] string? sortBy = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new GetBooksQuery(genreId, status, sortBy, page, pageSize));
        return Success(result);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BookSearchResultDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<BookSearchResultDto>>>> Search(
        [FromQuery] string? q,
        [FromQuery] string? genre,
        [FromQuery] string? tag,
        [FromQuery] string? author,
        [FromQuery] decimal? minRating,
        [FromQuery] string? language,
        [FromQuery] int? yearFrom,
        [FromQuery] int? yearTo,
        [FromQuery] string? sortBy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new SearchBooksFilter(q, genre, tag, author, minRating, language, yearFrom, yearTo, sortBy, page, pageSize);
        var result = await Mediator.Send(new SearchBooksQuery(filter));
        return Success(result);
    }

    [HttpGet("trending")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendedBookDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecommendedBookDto>>>> GetTrending([FromQuery] int limit = 10)
    {
        var result = await Mediator.Send(new GetTrendingBooksQuery(limit));
        return Success(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BookDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BookDetailDto>>> GetById(Guid id)
    {
        var result = await Mediator.Send(new GetBookByIdQuery(id));
        return Success(result);
    }

    [HttpGet("{id:guid}/similar")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendedBookDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecommendedBookDto>>>> GetSimilar(Guid id, [FromQuery] int limit = 6)
    {
        var result = await Mediator.Send(new GetSimilarBooksQuery(id, limit));
        return Success(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateBook([FromBody] CreateBookCommand command)
    {
        var bookId = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/books/{bookId}", bookId, "Book created successfully.");
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> UpdateBook(Guid id, [FromBody] UpdateBookCommand command)
    {
        if (id != command.Id) return BadRequest(ApiResponse.Fail("Route ID does not match body ID."));
        await Mediator.Send(command);
        return Success("Book updated successfully.");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> PublishBook(Guid id)
    {
        await Mediator.Send(new PublishBookCommand(id));
        return Success("Book published successfully.");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/editions")]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<Guid>>> AddEdition(Guid id, [FromBody] AddBookEditionCommand command)
    {
        if (id != command.BookId) return BadRequest(ApiResponse.Fail("Route ID does not match command BookId."));
        var editionId = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/books/{id}/editions/{editionId}", editionId, "Book edition added.");
    }

    // --- Reviews for Book ---
    [HttpGet("{id:guid}/reviews")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BookReviewDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<BookReviewDto>>>> GetReviews(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new GetBookReviewsQuery(id, page, pageSize));
        return Success(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/reviews")]
    [ProducesResponseType(typeof(ApiResponse<BookReviewDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<BookReviewDto>>> CreateReview(Guid id, [FromBody] CreateReviewRequest request)
    {
        var command = new CreateReviewCommand(id, request.Rating, request.Title, request.Content);
        var result = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/books/{id}/reviews/{result.Id}", result, "Review submitted successfully.");
    }

    // --- Favorite Book ---
    [Authorize]
    [HttpPost("{id:guid}/favorite")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> FavoriteBook(Guid id)
    {
        await Mediator.Send(new FavoriteBookCommand(id));
        return Success("Book added to favorites.");
    }

    [Authorize]
    [HttpDelete("{id:guid}/favorite")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> UnfavoriteBook(Guid id)
    {
        await Mediator.Send(new UnfavoriteBookCommand(id));
        return Success("Book removed from favorites.");
    }

    // --- Reading Progress ---
    [Authorize]
    [HttpPost("{id:guid}/progress")]
    [ProducesResponseType(typeof(ApiResponse<ReadingProgressResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReadingProgressResultDto>>> UpdateProgress(Guid id, [FromBody] UpdateProgressRequest request)
    {
        var result = await Mediator.Send(new UpdateReadingProgressCommand(id, request.CurrentPage));
        return Success(result, "Reading progress updated.");
    }
}

public record CreateReviewRequest(int Rating, string? Title, string? Content);
public record UpdateProgressRequest(int CurrentPage);
