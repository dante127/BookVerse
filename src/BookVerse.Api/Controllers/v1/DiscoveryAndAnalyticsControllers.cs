using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Analytics;
using BookVerse.Application.Features.Notifications;
using BookVerse.Application.Features.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookVerse.Api.Controllers.v1;

[Route("api/v1/recommendations")]
public class RecommendationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendedBookDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RecommendedBookDto>>>> GetRecommendations([FromQuery] int limit = 10)
    {
        var result = await Mediator.Send(new GetRecommendationsQuery(limit));
        return Success(result);
    }
}

[Authorize]
[Route("api/v1/notifications")]
public class NotificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NotificationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NotificationDto>>>> GetNotifications(
        [FromQuery] bool? onlyUnread = false,
        [FromQuery] int limit = 50)
    {
        var result = await Mediator.Send(new GetNotificationsQuery(onlyUnread, limit));
        return Success(result);
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> MarkAsRead(Guid id)
    {
        var success = await Mediator.Send(new MarkNotificationAsReadCommand(id));
        return Success(success ? "Notification marked as read." : "Notification not found.");
    }

    [HttpPost("read-all")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> MarkAllAsRead()
    {
        var count = await Mediator.Send(new MarkAllNotificationsAsReadCommand());
        return Success(count, $"{count} notifications marked as read.");
    }
}

[Route("api/v1/analytics")]
public class AnalyticsController : ApiControllerBase
{
    [Authorize]
    [HttpGet("reading")]
    [ProducesResponseType(typeof(ApiResponse<UserReadingAnalyticsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserReadingAnalyticsDto>>> GetUserReadingAnalytics()
    {
        var result = await Mediator.Send(new GetUserReadingAnalyticsQuery());
        return Success(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    [ProducesResponseType(typeof(ApiResponse<AdminAnalyticsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminAnalyticsDto>>> GetAdminAnalytics()
    {
        var result = await Mediator.Send(new GetAdminAnalyticsQuery());
        return Success(result);
    }
}
