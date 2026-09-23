using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Library;
using BookVerse.Application.Features.Reading;
using BookVerse.Application.Features.Reviews;
using BookVerse.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[Authorize]
[Route("api/v1/library")]
public class LibraryController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserBookItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserBookItemDto>>>> GetLibrary(
        [FromQuery] UserBookStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new GetUserLibraryQuery(status, page, pageSize));
        return Success(result);
    }

    [HttpGet("books/{bookId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserBookItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserBookItemDto>>> GetLibraryBook(Guid bookId)
    {
        var result = await Mediator.Send(new GetLibraryBookQuery(bookId));
        return Success(result);
    }

    [HttpPost("books")]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<Guid>>> AddToLibrary([FromBody] AddBookToLibraryCommand command)
    {
        var result = await Mediator.Send(command);

        // API-04: re-adding an existing entry is an idempotent status update, not a creation.
        if (!result.Created)
        {
            return Success(result.Id, "Book is already in your library; status updated.");
        }

        return CreatedSuccess($"/api/v1/library/books/{command.BookId}", result.Id, "Book added to your library.");
    }

    [HttpPut("books/{bookId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(Guid bookId, [FromBody] UpdateStatusRequest request)
    {
        await Mediator.Send(new UpdateUserBookStatusCommand(bookId, request.Status));
        return Success("Library entry status updated.");
    }

    [HttpDelete("books/{bookId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> RemoveFromLibrary(Guid bookId)
    {
        await Mediator.Send(new RemoveBookFromLibraryCommand(bookId));
        return Success("Book removed from library.");
    }
}

public record UpdateStatusRequest(UserBookStatus Status);

[Authorize]
[Route("api/v1/reading")]
public class ReadingController : ApiControllerBase
{
    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReadingHistoryItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<ReadingHistoryItemDto>>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new GetReadingHistoryQuery(page, pageSize));
        return Success(result);
    }

    [HttpGet("goals")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReadingGoalDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ReadingGoalDto>>>> GetGoals()
    {
        var result = await Mediator.Send(new GetReadingGoalsQuery());
        return Success(result);
    }

    [HttpPost("goals")]
    [ProducesResponseType(typeof(ApiResponse<ReadingGoalDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ReadingGoalDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ReadingGoalDto>>> SetGoal([FromBody] SetReadingGoalCommand command)
    {
        var result = await Mediator.Send(command);

        // API-04: POST goals is an upsert — 201 only when the goal was created.
        if (!result.Created)
        {
            return Success(result.Goal, "Reading goal updated.");
        }

        return CreatedSuccess("/api/v1/reading/goals", result.Goal, "Reading goal created.");
    }
}

[Route("api/v1/reviews")]
public class ReviewsController : ApiControllerBase
{
    [Authorize(Roles = "Admin,Moderator")]
    [HttpPut("{id:guid}/moderate")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> ModerateReview(Guid id, [FromBody] ModerateReviewRequest request)
    {
        var command = new ModerateReviewCommand(id, request.NewStatus, request.ModerationNote);
        await Mediator.Send(command);
        return Success("Review moderation status updated.");
    }
}

public record ModerateReviewRequest(ReviewStatus NewStatus, string? ModerationNote);
