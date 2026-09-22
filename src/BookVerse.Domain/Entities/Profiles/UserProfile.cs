using BookVerse.Domain.Common;
using BookVerse.Domain.Entities.Identity;

namespace BookVerse.Domain.Entities.Profiles;

public class UserProfile : AuditableEntity<Guid>
{
    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Bio { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string PreferredLanguage { get; private set; } = "en";

    public User User { get; private set; } = null!;

    // Serialized or collection-based preferences
    public string? FavoriteGenreIdsJson { get; private set; }
    public string? PreferredAuthorIdsJson { get; private set; }

    private UserProfile() { }

    public static UserProfile Create(Guid userId, string displayName, string? bio = null, string? avatarUrl = null, string preferredLanguage = "en")
    {
        return new UserProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = displayName.Trim(),
            Bio = bio?.Trim(),
            AvatarUrl = avatarUrl?.Trim(),
            PreferredLanguage = string.IsNullOrWhiteSpace(preferredLanguage) ? "en" : preferredLanguage.Trim().ToLowerInvariant(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string displayName, string? bio, string? avatarUrl, string preferredLanguage)
    {
        DisplayName = displayName.Trim();
        Bio = bio?.Trim();
        AvatarUrl = avatarUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            PreferredLanguage = preferredLanguage.Trim().ToLowerInvariant();
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPreferences(string? favoriteGenreIdsJson, string? preferredAuthorIdsJson)
    {
        FavoriteGenreIdsJson = favoriteGenreIdsJson;
        PreferredAuthorIdsJson = preferredAuthorIdsJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
