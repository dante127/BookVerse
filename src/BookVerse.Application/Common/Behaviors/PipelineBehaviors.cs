using System.Diagnostics;
using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BookVerse.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUserService;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId?.ToString() ?? "Anonymous";

        _logger.LogInformation("Handling {RequestName} for User: {UserId}", requestName, userId);

        var response = await next();

        _logger.LogInformation("Handled {RequestName} successfully", requestName);

        return response;
    }
}

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .Select(f => new ErrorDetail(f.PropertyName, f.ErrorMessage))
            .ToList();

        if (failures.Count != 0)
        {
            throw new Exceptions.ValidationException(failures);
        }

        return await next();
    }
}

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly Stopwatch _timer = new();
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUserService;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _timer.Restart();

        var response = await next();

        _timer.Stop();

        var elapsedMilliseconds = _timer.ElapsedMilliseconds;

        if (elapsedMilliseconds > 500)
        {
            var requestName = typeof(TRequest).Name;
            var userId = _currentUserService.UserId?.ToString() ?? "Anonymous";

            _logger.LogWarning(
                "Long Running Request: {RequestName} ({ElapsedMilliseconds} ms) by User: {UserId}",
                requestName, elapsedMilliseconds, userId);
        }

        return response;
    }
}
