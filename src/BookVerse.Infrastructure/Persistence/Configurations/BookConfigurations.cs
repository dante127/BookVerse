using BookVerse.Domain.Entities.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookVerse.Infrastructure.Persistence.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(b => b.Subtitle)
            .HasMaxLength(250);

        builder.Property(b => b.Description)
            .IsRequired();

        builder.Property(b => b.ISBN)
            .HasMaxLength(20);

        builder.HasIndex(b => b.ISBN)
            .IsUnique()
            .HasFilter("[ISBN] IS NOT NULL");

        builder.Property(b => b.Language)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("en");

        builder.Property(b => b.Publisher)
            .HasMaxLength(150);

        builder.Property(b => b.CoverImageUrl)
            .HasMaxLength(500);

        builder.Property(b => b.AverageRating)
            .HasPrecision(3, 2)
            .HasDefaultValue(0.00m);

        builder.Property(b => b.RatingsCount)
            .HasDefaultValue(0);

        builder.Property(b => b.ReviewsCount)
            .HasDefaultValue(0);

        builder.Property(b => b.Status)
            .IsRequired();

        // Optimistic Concurrency Token
        builder.Property(b => b.RowVersion)
            .IsRowVersion();

        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.AverageRating);
        builder.HasIndex(b => b.PublicationDate);
        builder.HasIndex(b => b.CreatedAt);

        builder.HasMany(b => b.Editions)
            .WithOne(e => e.Book)
            .HasForeignKey(e => e.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Authors)
            .WithOne(ba => ba.Book)
            .HasForeignKey(ba => ba.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Genres)
            .WithOne(bg => bg.Book)
            .HasForeignKey(bg => bg.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Tags)
            .WithOne(bt => bt.Book)
            .HasForeignKey(bt => bt.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BookEditionConfiguration : IEntityTypeConfiguration<BookEdition>
{
    public void Configure(EntityTypeBuilder<BookEdition> builder)
    {
        builder.ToTable("BookEditions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ISBN)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(e => e.ISBN)
            .IsUnique();

        builder.Property(e => e.Format)
            .IsRequired();

        builder.Property(e => e.Publisher)
            .HasMaxLength(150);

        builder.Property(e => e.Language)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.FileUrl)
            .HasMaxLength(500);

        builder.HasIndex(e => e.BookId);
    }
}

public class BookAuthorConfiguration : IEntityTypeConfiguration<BookAuthor>
{
    public void Configure(EntityTypeBuilder<BookAuthor> builder)
    {
        builder.ToTable("BookAuthors");

        builder.HasKey(ba => new { ba.BookId, ba.AuthorId, ba.Role });

        builder.HasOne(ba => ba.Book)
            .WithMany(b => b.Authors)
            .HasForeignKey(ba => ba.BookId);

        builder.HasOne(ba => ba.Author)
            .WithMany()
            .HasForeignKey(ba => ba.AuthorId);

        builder.HasIndex(ba => ba.AuthorId);
    }
}

public class BookGenreConfiguration : IEntityTypeConfiguration<BookGenre>
{
    public void Configure(EntityTypeBuilder<BookGenre> builder)
    {
        builder.ToTable("BookGenres");

        builder.HasKey(bg => new { bg.BookId, bg.GenreId });

        builder.HasOne(bg => bg.Book)
            .WithMany(b => b.Genres)
            .HasForeignKey(bg => bg.BookId);

        builder.HasOne(bg => bg.Genre)
            .WithMany()
            .HasForeignKey(bg => bg.GenreId);

        builder.HasIndex(bg => bg.GenreId);
    }
}

public class BookTagConfiguration : IEntityTypeConfiguration<BookTag>
{
    public void Configure(EntityTypeBuilder<BookTag> builder)
    {
        builder.ToTable("BookTags");

        builder.HasKey(bt => new { bt.BookId, bt.TagId });

        builder.HasOne(bt => bt.Book)
            .WithMany(b => b.Tags)
            .HasForeignKey(bt => bt.BookId);

        builder.HasOne(bt => bt.Tag)
            .WithMany()
            .HasForeignKey(bt => bt.TagId);

        builder.HasIndex(bt => bt.TagId);
    }
}
