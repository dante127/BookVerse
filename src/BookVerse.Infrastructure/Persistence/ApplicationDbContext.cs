using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Audit;
using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Notifications;
using BookVerse.Domain.Entities.Profiles;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;

namespace BookVerse.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookEdition> BookEditions => Set<BookEdition>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();
    public DbSet<AuthorFollower> AuthorFollowers => Set<AuthorFollower>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<BookGenre> BookGenres => Set<BookGenre>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<BookTag> BookTags => Set<BookTag>();

    public DbSet<UserBook> UserBooks => Set<UserBook>();
    public DbSet<FavoriteBook> FavoriteBooks => Set<FavoriteBook>();
    public DbSet<ReadingProgress> ReadingProgresses => Set<ReadingProgress>();
    public DbSet<ReadingHistory> ReadingHistories => Set<ReadingHistory>();
    public DbSet<ReadingGoal> ReadingGoals => Set<ReadingGoal>();
    public DbSet<BookReview> BookReviews => Set<BookReview>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var dateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            var nullableDateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, long?>(
                v => v.HasValue ? v.Value.UtcTicks : null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var rowVersionProp = entity.FindProperty("RowVersion");
                if (rowVersionProp != null)
                {
                    rowVersionProp.SetDefaultValueSql("randomblob(8)");
                }

                foreach (var property in entity.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(dateTimeOffsetConverter);
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableDateTimeOffsetConverter);
                    }
                }
            }
        }
    }
}
