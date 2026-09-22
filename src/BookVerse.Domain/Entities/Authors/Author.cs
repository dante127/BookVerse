using BookVerse.Domain.Common;

namespace BookVerse.Domain.Entities.Authors;

public class Author : AuditableEntity<Guid>
{
    private readonly List<AuthorFollower> _followers = [];

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Biography { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? Country { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? ProfileImageUrl { get; private set; }
    public int FollowersCount { get; private set; } = 0;

    public IReadOnlyCollection<AuthorFollower> Followers => _followers.AsReadOnly();

    private Author() { }

    public static Author Create(string name, string slug, string? biography = null, DateOnly? birthDate = null, string? country = null, string? websiteUrl = null, string? profileImageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Author name cannot be empty.", nameof(name));

        return new Author
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Biography = biography?.Trim(),
            BirthDate = birthDate,
            Country = country?.Trim(),
            WebsiteUrl = websiteUrl?.Trim(),
            ProfileImageUrl = profileImageUrl?.Trim(),
            FollowersCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string name, string slug, string? biography, DateOnly? birthDate, string? country, string? websiteUrl, string? profileImageUrl)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Biography = biography?.Trim();
        BirthDate = birthDate;
        Country = country?.Trim();
        WebsiteUrl = websiteUrl?.Trim();
        ProfileImageUrl = profileImageUrl?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void IncrementFollowers() => FollowersCount++;
    public void DecrementFollowers() { if (FollowersCount > 0) FollowersCount--; }
}

public class AuthorFollower
{
    public Guid UserId { get; private set; }
    public Guid AuthorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public Author Author { get; private set; } = null!;

    private AuthorFollower() { }

    public AuthorFollower(Guid userId, Guid authorId)
    {
        UserId = userId;
        AuthorId = authorId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
