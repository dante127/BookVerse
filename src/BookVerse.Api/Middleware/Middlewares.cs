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
        // L-02: a client-supplied id is only honored when it looks like one —
        // bounded length and a safe alphabet — otherwise it is log/response forging.
        var incoming = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        var correlationId = IsValidCorrelationId(incoming) ? incoming! : Guid.NewGuid().ToString("N");

        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static bool IsValidCorrelationId(string? value)
        => value is { Length: >= 8 and <= 64 }
           && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
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

            case DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx):
                statusCode = HttpStatusCode.Conflict;
                message = "A record with the same unique value (for example ISBN) already exists.";
                _logger.LogWarning(dbEx, "Unique constraint violation on {Entities}",
                    string.Join(", ", dbEx.Entries.Select(e => e.Entity.GetType().Name)));
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

    // Providers surface unique violations only through the inner exception message:
    // SQL Server (2627/2601) and SQLite phrase this differently.
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique index", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
