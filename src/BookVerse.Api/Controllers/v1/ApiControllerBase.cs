using BookVerse.Application.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Mediator => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null)
    {
        return Ok(ApiResponse<T>.Ok(data, message));
    }

    protected ActionResult<ApiResponse> Success(string? message = null)
    {
        return Ok(ApiResponse.Ok(message));
    }

    protected ActionResult<ApiResponse<T>> CreatedSuccess<T>(string uri, T data, string? message = null)
    {
        return Created(uri, ApiResponse<T>.Ok(data, message));
    }
}
