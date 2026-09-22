using BookVerse.Application.Common.Models;

namespace BookVerse.Application.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string name, object key) : base($"Entity '{name}' with key '{key}' was not found.") { }
}

public class ValidationException : Exception
{
    public List<ErrorDetail> Errors { get; }

    public ValidationException(List<ErrorDetail> errors) : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string field, string message) : base("A validation error occurred.")
    {
        Errors = [new ErrorDetail(field, message)];
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.") : base(message) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication failed or token is invalid.") : base(message) { }
}
