using BookVerse.Domain.Common;

namespace BookVerse.Domain.Entities.Tags;

public class Tag : Entity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;

    private Tag() { }

    public static Tag Create(string name, string slug)
    {
        return new Tag
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant()
        };
    }
}
