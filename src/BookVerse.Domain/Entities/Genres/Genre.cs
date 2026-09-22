using BookVerse.Domain.Common;

namespace BookVerse.Domain.Entities.Genres;

public class Genre : AuditableEntity<Guid>
{
    private readonly List<Genre> _subGenres = [];

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentGenreId { get; private set; }

    public Genre? ParentGenre { get; private set; }
    public IReadOnlyCollection<Genre> SubGenres => _subGenres.AsReadOnly();

    private Genre() { }

    public static Genre Create(string name, string slug, string? description = null, Guid? parentGenreId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Genre name cannot be empty.", nameof(name));

        return new Genre
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            ParentGenreId = parentGenreId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(string name, string slug, string? description, Guid? parentGenreId)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description?.Trim();
        ParentGenreId = parentGenreId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
