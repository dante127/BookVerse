using BookVerse.Domain.Entities.Audit;
using BookVerse.Domain.Entities.Notifications;
using BookVerse.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookVerse.Infrastructure.Persistence.Configurations;

public class BookReviewConfiguration : IEntityTypeConfiguration<BookReview>
{
    public void Configure(EntityTypeBuilder<BookReview> builder)
    {
        builder.ToTable("BookReviews");

        builder.HasKey(r => r.Id);

        builder.HasIndex(r => new { r.BookId, r.UserId })
            .IsUnique();

        builder.Property(r => r.Rating)
            .IsRequired();

        builder.Property(r => r.Title)
            .HasMaxLength(150);

        builder.Property(r => r.Content)
            .HasMaxLength(4000);

        builder.Property(r => r.Status)
            .IsRequired();

        builder.Property(r => r.ModerationNote)
            .HasMaxLength(500);

        // Optimistic Concurrency Token
        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        builder.HasOne(r => r.Book)
            .WithMany()
            .HasForeignKey(r => r.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.BookId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreatedAt);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Message)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(n => n.ReferenceUrl)
            .HasMaxLength(500);

        builder.Property(n => n.Type)
            .IsRequired();

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.HasIndex(n => n.CreatedAt);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.HasIndex(a => new { a.EntityName, a.EntityId });
        builder.HasIndex(a => a.Timestamp);
    }
}
