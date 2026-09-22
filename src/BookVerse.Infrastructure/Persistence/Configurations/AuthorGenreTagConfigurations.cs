using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookVerse.Infrastructure.Persistence.Configurations;

public class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(a => a.Slug)
            .IsRequired()
            .HasMaxLength(160);

        builder.HasIndex(a => a.Slug)
            .IsUnique();

        builder.Property(a => a.Country)
            .HasMaxLength(100);

        builder.Property(a => a.WebsiteUrl)
            .HasMaxLength(500);

        builder.Property(a => a.ProfileImageUrl)
            .HasMaxLength(500);

        builder.Property(a => a.FollowersCount)
            .HasDefaultValue(0);

        builder.HasIndex(a => a.Name);
        builder.HasIndex(a => a.FollowersCount);

        builder.HasMany(a => a.Followers)
            .WithOne(af => af.Author)
            .HasForeignKey(af => af.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuthorFollowerConfiguration : IEntityTypeConfiguration<AuthorFollower>
{
    public void Configure(EntityTypeBuilder<AuthorFollower> builder)
    {
        builder.ToTable("AuthorFollowers");

        builder.HasKey(af => new { af.UserId, af.AuthorId });

        builder.HasOne(af => af.Author)
            .WithMany(a => a.Followers)
            .HasForeignKey(af => af.AuthorId);
    }
}

public class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(g => g.Slug)
            .IsRequired()
            .HasMaxLength(110);

        builder.HasIndex(g => g.Slug)
            .IsUnique();

        builder.Property(g => g.Description)
            .HasMaxLength(500);

        builder.HasOne(g => g.ParentGenre)
            .WithMany(g => g.SubGenres)
            .HasForeignKey(g => g.ParentGenreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(60);

        builder.HasIndex(t => t.Slug)
            .IsUnique();
    }
}
