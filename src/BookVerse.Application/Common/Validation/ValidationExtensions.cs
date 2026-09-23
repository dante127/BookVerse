using FluentValidation;

namespace BookVerse.Application.Common.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// SEC-08: URL fields are echoed to web clients, so only absolute http(s) URLs
    /// are accepted — this rejects javascript:, data:, ftp: and relative paths.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsValidWebUrl<T>(this IRuleBuilder<T, string?> ruleBuilder)
        => ruleBuilder
            .Must(url => string.IsNullOrWhiteSpace(url) || IsHttpUrl(url))
            .WithMessage("'{PropertyName}' must be an absolute http or https URL.");

    /// <summary>
    /// ISBN-10 (hyphens optional, trailing check digit may be X) or ISBN-13 digits.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsValidIsbn<T>(this IRuleBuilder<T, string?> ruleBuilder)
        => ruleBuilder
            .Must(IsValidIsbnValue)
            .WithMessage("'{PropertyName}' must be a valid ISBN-10 or ISBN-13.");

    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsValidIsbnValue(string? isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn)) return true;

        var digits = isbn.Replace("-", "").Trim();
        return (digits.Length == 10
                    && long.TryParse(digits.AsSpan(0, 9), out _)
                    && (digits[9] == 'X' || digits[9] == 'x' || char.IsAsciiDigit(digits[9])))
               || (digits.Length == 13 && long.TryParse(digits, out _));
    }
}
