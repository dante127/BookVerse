using System.Globalization;
using System.Text;
using BookVerse.Application.Common.Exceptions;

namespace BookVerse.Application.Common.Services;

/// <summary>
/// CQ-02: one slug algorithm shared by Author/Genre/Tag creation. The previous inline
/// ToLowerInvariant().Replace(' ', '-') let punctuation and "../" segments through into
/// URL-bearing slugs. Slugs stay ASCII alphanumeric with single hyphen separators.
/// </summary>
public static class Slug
{
    public static string Slugify(string input)
    {
        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(normalized.Length);
        var pendingHyphen = false;

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && slug.Length > 0)
                    slug.Append('-');
                slug.Append(ch);
                pendingHyphen = false;
            }
            else
            {
                // Whitespace, hyphens, and all dropped characters (punctuation,
                // non-ASCII letters) act as single separators: "etc/passwd" -> "etc-passwd".
                pendingHyphen = slug.Length > 0;
            }
        }

        var result = slug.ToString();
        if (result.Length == 0)
            throw new ValidationException("name", "The name must contain letters or digits to generate a slug.");

        return result;
    }
}
