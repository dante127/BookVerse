namespace BookVerse.Domain.Exceptions;

public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string message, string code = "Domain.Error") : base(message)
    {
        Code = code;
    }
}

public class BookDomainException(string message) : DomainException(message, "Book.DomainError");
public class ReviewDomainException(string message) : DomainException(message, "Review.DomainError");
public class ReadingDomainException(string message) : DomainException(message, "Reading.DomainError");
public class IdentityDomainException(string message) : DomainException(message, "Identity.DomainError");
