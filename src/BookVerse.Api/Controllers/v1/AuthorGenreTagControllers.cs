using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Authors;
using BookVerse.Application.Features.Genres;
using BookVerse.Application.Features.Tags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[Route("api/v1/authors")]
public class AuthorsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuthorSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuthorSummaryDto>>>> GetAuthors(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new GetAuthorsQuery(search, page, pageSize));
        return Success(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AuthorDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuthorDetailDto>>> GetById(Guid id)
    {
        var result = await Mediator.Send(new GetAuthorByIdQuery(id));
        return Success(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateAuthor([FromBody] CreateAuthorCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/authors/{id}", id, "Author created successfully.");
    }

    [Authorize]
    [HttpPost("{id:guid}/follow")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Follow(Guid id)
    {
        await Mediator.Send(new FollowAuthorCommand(id));
        return Success("Author followed successfully.");
    }

    [Authorize]
    [HttpDelete("{id:guid}/follow")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Unfollow(Guid id)
    {
        await Mediator.Send(new UnfollowAuthorCommand(id));
        return Success("Author unfollowed.");
    }
}

[Route("api/v1/genres")]
public class GenresController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<GenreItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GenreItemDto>>>> GetGenres()
    {
        var result = await Mediator.Send(new GetGenresQuery());
        return Success(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateGenre([FromBody] CreateGenreCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/genres/{id}", id, "Genre created successfully.");
    }
}

[Route("api/v1/tags")]
public class TagsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TagDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TagDto>>>> GetTags()
    {
        var result = await Mediator.Send(new GetTagsQuery());
        return Success(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<Guid>>> CreateTag([FromBody] CreateTagCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/tags/{id}", id, "Tag created successfully.");
    }
}
