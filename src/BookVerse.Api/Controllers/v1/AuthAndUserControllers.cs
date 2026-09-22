using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterCommand command)
    {
        var result = await Mediator.Send(command);
        return CreatedSuccess($"/api/v1/users/{result.UserId}", result, "User registered successfully.");
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginCommand command)
    {
        var result = await Mediator.Send(command);
        return Success(result, "Authentication successful.");
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> RefreshToken([FromBody] RefreshTokenCommand command)
    {
        var result = await Mediator.Send(command);
        return Success(result, "Token refreshed successfully.");
    }

    [Authorize]
    [HttpPost("revoke")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> RevokeToken([FromBody] RevokeTokenCommand command)
    {
        var revoked = await Mediator.Send(command);
        return Success(revoked ? "Token revoked successfully." : "Token was not found or already inactive.");
    }
}

[Authorize]
[Route("api/v1/users")]
public class UsersController : ApiControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetMe()
    {
        var result = await Mediator.Send(new GetCurrentUserQuery());
        return Success(result);
    }

    [HttpPut("me/profile")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> UpdateProfile([FromBody] UpdateUserProfileCommand command)
    {
        var result = await Mediator.Send(command);
        return Success(result, "Profile updated successfully.");
    }

    [HttpPut("me/preferences")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> UpdatePreferences([FromBody] UpdateReadingPreferencesCommand command)
    {
        await Mediator.Send(command);
        return Success("Reading preferences updated successfully.");
    }
}
