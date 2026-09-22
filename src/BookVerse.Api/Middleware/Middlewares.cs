using System.Net;
using System.Text.Json;
using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Models;
using BookVerse.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace BookVerse.Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = HttpStatusCode.InternalServerError;
        var message = "An unexpected error occurred.";
        List<ErrorDetail> errors = [];

        switch (exception)
        {
            case ValidationException valEx:
                statusCode = HttpStatusCode.BadRequest;
                message = valEx.Message;
                errors = valEx.Errors;
                _logger.LogWarning("Validation failure: {Message} Errors: {@Errors}", message, errors);
                break;

            case NotFoundException notFoundEx:
                statusCode = HttpStatusCode.NotFound;
                message = notFoundEx.Message;
                _logger.LogWarning("Resource not found: {Message}", message);
                break;

            case ConflictException conflictEx:
                statusCode = HttpStatusCode.Conflict;
                message = conflictEx.Message;
                _logger.LogWarning("Conflict detected: {Message}", message);
                break;

            case DbUpdateConcurrencyException concurrencyEx:
                statusCode = HttpStatusCode.Conflict;
                message = "The resource has been updated or modified by another transaction. Please reload and retry.";
                _logger.LogWarning(concurrencyEx, "Concurrency conflict occurred on {Entities}",
                    string.Join(", ", concurrencyEx.Entries.Select(e => e.Entity.GetType().Name)));
                break;

            case DomainException domainEx:
                statusCode = HttpStatusCode.UnprocessableEntity;
                message = domainEx.Message;
                _logger.LogWarning("Domain rule violated: [{Code}] {Message}", domainEx.Code, message);
                break;

            case ForbiddenException forbiddenEx:
                statusCode = HttpStatusCode.Forbidden;
                message = forbiddenEx.Message;
                _logger.LogWarning("Forbidden access attempt: {Message}", message);
                break;

            case UnauthorizedException unauthEx:
                statusCode = HttpStatusCode.Unauthorized;
                message = unauthEx.Message;
                _logger.LogWarning("Unauthorized access attempt: {Message}", message);
                break;

            default:
                _logger.LogError(exception, "Unhandled system exception: {Message}", exception.Message);
                message = "A critical server error occurred. Please contact system support.";
                break;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse.Fail(message, errors);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
