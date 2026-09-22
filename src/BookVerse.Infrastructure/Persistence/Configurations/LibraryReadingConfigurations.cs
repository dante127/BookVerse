using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookVerse.Infrastructure.Persistence.Configurations;

public class UserBookConfiguration : IEntityTypeConfiguration<UserBook>
{
    public void Configure(EntityTypeBuilder<UserBook> builder)
    {
        builder.ToTable("UserBooks");

        builder.HasKey(ub => ub.Id);

        builder.HasIndex(ub => new { ub.UserId, ub.BookId })
            .IsUnique();

        builder.Property(ub => ub.Status)
            .IsRequired();

        builder.HasOne(ub => ub.User)
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ub => ub.Book)
            .WithMany()
            .HasForeignKey(ub => ub.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ub => ub.Status);
        builder.HasIndex(ub => ub.LastReadAt);
    }
}

public class FavoriteBookConfiguration : IEntityTypeConfiguration<FavoriteBook>
{
    public void Configure(EntityTypeBuilder<FavoriteBook> builder)
    {
        builder.ToTable("FavoriteBooks");

        builder.HasKey(fb => new { fb.UserId, fb.BookId });

        builder.HasOne(fb => fb.User)
            .WithMany()
            .HasForeignKey(fb => fb.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(fb => fb.Book)
            .WithMany()
            .HasForeignKey(fb => fb.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReadingProgress> builder)
    {
        builder.ToTable("ReadingProgresses");

        builder.HasKey(rp => rp.Id);

        builder.HasIndex(rp => new { rp.UserId, rp.BookId })
            .IsUnique();

        builder.Property(rp => rp.Percentage)
            .HasPrecision(5, 2);

        // Optimistic concurrency token
        builder.Property(rp => rp.RowVersion)
            .IsRowVersion();

        builder.HasOne(rp => rp.User)
            .WithMany()
            .HasForeignKey(rp => rp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rp => rp.Book)
            .WithMany()
            .HasForeignKey(rp => rp.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rp => rp.LastReadAt);
    }
}

public class ReadingHistoryConfiguration : IEntityTypeConfiguration<ReadingHistory>
{
    public void Configure(EntityTypeBuilder<ReadingHistory> builder)
    {
        builder.ToTable("ReadingHistories");

        builder.HasKey(rh => rh.Id);

        builder.HasOne(rh => rh.User)
            .WithMany()
            .HasForeignKey(rh => rh.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rh => rh.Book)
            .WithMany()
            .HasForeignKey(rh => rh.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rh => rh.UserId);
        builder.HasIndex(rh => rh.Timestamp);
    }
}

public class ReadingGoalConfiguration : IEntityTypeConfiguration<ReadingGoal>
{
    public void Configure(EntityTypeBuilder<ReadingGoal> builder)
    {
        builder.ToTable("ReadingGoals");

        builder.HasKey(rg => rg.Id);

        builder.HasIndex(rg => new { rg.UserId, rg.Year })
            .IsUnique();

        builder.HasOne(rg => rg.User)
            .WithMany()
            .HasForeignKey(rg => rg.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
