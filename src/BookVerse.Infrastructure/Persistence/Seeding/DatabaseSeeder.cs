using BookVerse.Application.Common.Interfaces;
using BookVerse.Domain.Entities.Authors;
using BookVerse.Domain.Entities.Books;
using BookVerse.Domain.Entities.Genres;
using BookVerse.Domain.Entities.Identity;
using BookVerse.Domain.Entities.Library;
using BookVerse.Domain.Entities.Profiles;
using BookVerse.Domain.Entities.Reading;
using BookVerse.Domain.Entities.Reviews;
using BookVerse.Domain.Entities.Tags;
using BookVerse.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookVerse.Infrastructure.Persistence.Seeding;

public class DatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseSeeder(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<DatabaseSeeder> logger,
        IConfiguration configuration)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Roles and permissions are always seeded. A bootstrap admin is created only when
    /// Seed:AdminEmail + Seed:AdminPassword are configured. Sample content (users, books,
    /// reviews) is written only when withSampleData is true (Development / tests).
    /// </summary>
    public async Task SeedAsync(bool withSampleData = true)
    {
        await SeedRolesAndPermissionsAsync();
        await SeedBootstrapAdminAsync();

        if (withSampleData)
        {
            await SeedSampleDataAsync();
        }
    }

    private async Task<Role?> GetRoleAsync(string roleName)
    {
        return await _context.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Name == roleName);
    }

    private async Task SeedRolesAndPermissionsAsync()
    {
        if (await _context.Roles.AnyAsync())
            return;

        _logger.LogInformation("Seeding permissions and roles...");

        var permissions = new[]
        {
            new Permission(Guid.NewGuid(), "books:create", "Create new books"),
            new Permission(Guid.NewGuid(), "books:edit", "Edit book details"),
            new Permission(Guid.NewGuid(), "books:publish", "Publish draft books"),
            new Permission(Guid.NewGuid(), "reviews:create", "Write reviews"),
            new Permission(Guid.NewGuid(), "reviews:moderate", "Approve or reject reviews"),
            new Permission(Guid.NewGuid(), "library:manage", "Manage personal library"),
            new Permission(Guid.NewGuid(), "reading:track", "Track reading progress"),
            new Permission(Guid.NewGuid(), "authors:follow", "Follow authors"),
            new Permission(Guid.NewGuid(), "analytics:admin", "View admin platform metrics")
        };
        _context.Permissions.AddRange(permissions);

        var adminRole = new Role(Guid.NewGuid(), "Admin", "System administrator with full privileges");
        foreach (var p in permissions) adminRole.AddPermission(p);

        var moderatorRole = new Role(Guid.NewGuid(), "Moderator", "Content and review moderator");
        foreach (var p in permissions.Where(x => x.Name.StartsWith("reviews:") || x.Name.StartsWith("library:") || x.Name.StartsWith("reading:")))
        {
            moderatorRole.AddPermission(p);
        }

        var readerRole = new Role(Guid.NewGuid(), "Reader", "Standard reader user");
        foreach (var p in permissions.Where(x => x.Name.StartsWith("library:") || x.Name.StartsWith("reading:") || x.Name == "reviews:create" || x.Name == "authors:follow"))
        {
            readerRole.AddPermission(p);
        }

        _context.Roles.AddRange(adminRole, moderatorRole, readerRole);
        await _context.SaveChangesAsync();
    }

    private async Task SeedBootstrapAdminAsync()
    {
        var adminEmail = _configuration["Seed:AdminEmail"];
        var adminPassword = _configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            return;

        var normalizedEmail = adminEmail.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email == normalizedEmail))
            return;

        var adminRole = await GetRoleAsync("Admin")
            ?? throw new InvalidOperationException("Admin role missing; SeedRolesAndPermissionsAsync must run first.");

        var (hash, salt) = _passwordHasher.HashPassword(adminPassword);
        var adminUser = User.Create(normalizedEmail, hash, salt);
        adminUser.AddRole(adminRole);
        _context.Users.Add(adminUser);
        _context.UserProfiles.Add(UserProfile.Create(adminUser.Id, "Platform Administrator"));
        await _context.SaveChangesAsync();

        _logger.LogInformation("Bootstrap admin account created from Seed configuration.");
    }

    private async Task SeedSampleDataAsync()
    {
        if (await _context.Users.AnyAsync())
        {
            _logger.LogInformation("Users already present. Skipping sample data seeding.");
            return;
        }

        _logger.LogInformation("Seeding database with realistic development data...");

        var adminRole = await GetRoleAsync("Admin")
            ?? throw new InvalidOperationException("Admin role missing; SeedRolesAndPermissionsAsync must run first.");
        var readerRole = await GetRoleAsync("Reader")
            ?? throw new InvalidOperationException("Reader role missing; SeedRolesAndPermissionsAsync must run first.");

        // 1. Users
        var devAdminPassword = _configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(devAdminPassword))
        {
            devAdminPassword = "Admin12345!";
            _logger.LogWarning("Development sample admin seeded with the well-known default password. Set Seed:AdminPassword to override.");
        }

        var (adminHash, adminSalt) = _passwordHasher.HashPassword(devAdminPassword);
        var adminUser = User.Create("admin@bookverse.io", adminHash, adminSalt);
        adminUser.AddRole(adminRole);
        var adminProfile = UserProfile.Create(adminUser.Id, "Admin Supreme", "Platform Architect & Curator");

        var (reader1Hash, reader1Salt) = _passwordHasher.HashPassword("Reader12345!");
        var reader1 = User.Create("elena.rostova@bookverse.io", reader1Hash, reader1Salt);
        reader1.AddRole(readerRole);
        var profile1 = UserProfile.Create(reader1.Id, "Elena Rostova", "Avid sci-fi & epic fantasy explorer.");

        var (reader2Hash, reader2Salt) = _passwordHasher.HashPassword("Reader12345!");
        var reader2 = User.Create("marcus.vance@bookverse.io", reader2Hash, reader2Salt);
        reader2.AddRole(readerRole);
        var profile2 = UserProfile.Create(reader2.Id, "Marcus Vance", "Cyberpunk, dark mystery, and thriller enthusiast.");

        _context.Users.AddRange(adminUser, reader1, reader2);
        _context.UserProfiles.AddRange(adminProfile, profile1, profile2);

        // 2. Hierarchical Genres
        var fiction = Genre.Create("Fiction", "fiction", "Works created from imagination.");
        var nonFiction = Genre.Create("Non-Fiction", "non-fiction", "Factual prose writing.");
        _context.Genres.AddRange(fiction, nonFiction);
        await _context.SaveChangesAsync();

        var fantasy = Genre.Create("Fantasy", "fantasy", "Magic and mythical storytelling", fiction.Id);
        var sciFi = Genre.Create("Science Fiction", "sci-fi", "Speculative futures and technological wonders", fiction.Id);
        var mystery = Genre.Create("Mystery & Thriller", "mystery-thriller", "Suspense, crimes, and puzzles", fiction.Id);
        _context.Genres.AddRange(fantasy, sciFi, mystery);
        await _context.SaveChangesAsync();

        var epicFantasy = Genre.Create("Epic Fantasy", "epic-fantasy", "Vast worldbuilding and multi-volume sagas", fantasy.Id);
        var darkFantasy = Genre.Create("Dark Fantasy", "dark-fantasy", "Grim and foreboding fantasy tales", fantasy.Id);
        var cyberpunk = Genre.Create("Cyberpunk", "cyberpunk", "High tech, low life dystopian fiction", sciFi.Id);
        var spaceOpera = Genre.Create("Space Opera", "space-opera", "Interstellar conflict and spacefaring civilizations", sciFi.Id);
        _context.Genres.AddRange(epicFantasy, darkFantasy, cyberpunk, spaceOpera);

        // 3. Tags
        var tagMagic = Tag.Create("Magic", "magic");
        var tagDragons = Tag.Create("Dragons", "dragons");
        var tagAI = Tag.Create("Artificial Intelligence", "artificial-intelligence");
        var tagCybernetics = Tag.Create("Cybernetics", "cybernetics");
        var tagDetective = Tag.Create("Detective", "detective");
        var tagTimeTravel = Tag.Create("Time Travel", "time-travel");
        var tagSlowBurn = Tag.Create("Slow Burn", "slow-burn");
        _context.Tags.AddRange(tagMagic, tagDragons, tagAI, tagCybernetics, tagDetective, tagTimeTravel, tagSlowBurn);

        // 4. Authors
        var author1 = Author.Create("Brandon Sanderson", "brandon-sanderson", "Acclaimed epic fantasy master, creator of the Cosmere universe.", new DateOnly(1975, 12, 19), "United States", "https://brandonsanderson.com");
        var author2 = Author.Create("William Gibson", "william-gibson", "Pioneering science fiction author who coined the term cyberspace.", new DateOnly(1948, 3, 17), "United States", "https://williamgibsonbooks.com");
        var author3 = Author.Create("Agatha Christie", "agatha-christie", "The Queen of Mystery, creator of Hercule Poirot and Miss Marple.", new DateOnly(1890, 9, 15), "United Kingdom");
        var author4 = Author.Create("Cixin Liu", "cixin-liu", "Renowned Chinese hard sci-fi writer, author of the Remembrance of Earth's Past trilogy.", new DateOnly(1963, 6, 23), "China");
        _context.Authors.AddRange(author1, author2, author3, author4);
        await _context.SaveChangesAsync();

        // 5. Books
        var book1 = Book.Create(
            "The Way of Kings",
            "Roshar is a world of stone and storms. Uncanny tempests of incredible power sweep across the rocky terrain. Centuries have passed since the fall of the ten consecrated orders known as the Knights Radiant, but their Shardblades and Shardplate remain.",
            1007,
            "The Stormlight Archive, Book 1",
            "9780765326355",
            new DateOnly(2010, 8, 31),
            "en",
            "Tor Books",
            "https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c");
        book1.AddAuthor(author1, AuthorRole.Author);
        book1.AddGenre(fantasy);
        book1.AddGenre(epicFantasy);
        book1.AddTag(tagMagic);
        book1.Publish();
        book1.AddEdition("9780765326355", BookEditionFormat.Hardcover, "Tor Books", new DateOnly(2010, 8, 31), 1007, "en");
        book1.AddEdition("9780765365279", BookEditionFormat.Paperback, "Tor Books", new DateOnly(2011, 5, 24), 1280, "en");
        book1.AddEdition("9781429998406", BookEditionFormat.Ebook, "Tor Books", new DateOnly(2010, 8, 31), 1007, "en");

        var book2 = Book.Create(
            "Neuromancer",
            "Case was the sharpest data-thief in the matrix—until he crossed the wrong people and they crippled his nervous system. Now a mysterious employer recruits him for a last-chance run at an unthinkably powerful artificial intelligence.",
            271,
            "The Sprawl Trilogy, Book 1",
            "9780441569595",
            new DateOnly(1984, 7, 1),
            "en",
            "Ace Books",
            "https://images.unsplash.com/photo-1526374965328-7f61d4dc18c5");
        book2.AddAuthor(author2, AuthorRole.Author);
        book2.AddGenre(sciFi);
        book2.AddGenre(cyberpunk);
        book2.AddTag(tagAI);
        book2.AddTag(tagCybernetics);
        book2.Publish();
        book2.AddEdition("9780441569595", BookEditionFormat.Paperback, "Ace Books", new DateOnly(1984, 7, 1), 271, "en");

        var book3 = Book.Create(
            "The Three-Body Problem",
            "Set against the backdrop of China's Cultural Revolution, a secret military project sends signals into space to establish contact with aliens. An alien civilization on the brink of destruction captures the signal and plans to invade Earth.",
            390,
            "Remembrance of Earth's Past",
            "9780765377067",
            new DateOnly(2008, 1, 1),
            "en",
            "Tor Books",
            "https://images.unsplash.com/photo-1451187580459-43490279c0fa");
        book3.AddAuthor(author4, AuthorRole.Author);
        book3.AddGenre(sciFi);
        book3.AddGenre(spaceOpera);
        book3.AddTag(tagAI);
        book3.Publish();
        book3.AddEdition("9780765377067", BookEditionFormat.Hardcover, "Tor Books", new DateOnly(2014, 11, 11), 390, "en");

        var book4 = Book.Create(
            "And Then There Were None",
            "Ten strangers are invited to an isolated island mansion off the Devon coast by a mysterious host. One by one, they are accused of crimes and met with untimely demises matching an eerie nursery rhyme.",
            272,
            null,
            "9780062073488",
            new DateOnly(1939, 11, 6),
            "en",
            "Collins Crime Club",
            "https://images.unsplash.com/photo-1589829085413-56de8ae18c73");
        book4.AddAuthor(author3, AuthorRole.Author);
        book4.AddGenre(mystery);
        book4.AddTag(tagDetective);
        book4.Publish();
        book4.AddEdition("9780062073488", BookEditionFormat.Paperback, "William Morrow", new DateOnly(2011, 3, 29), 272, "en");

        // Draft book: intentionally left unpublished to verify visibility enforcement.
        var draftBook = Book.Create(
            "The Knights of the Draft (Unpublished Manuscript)",
            "An unedited early draft under internal review. Never intended for public catalog exposure until publication.",
            640,
            null,
            null,
            null,
            "en",
            null,
            null);
        draftBook.AddAuthor(author1, AuthorRole.Author);
        draftBook.AddGenre(fantasy);
        draftBook.AddTag(tagSlowBurn);

        _context.Books.AddRange(book1, book2, book3, book4, draftBook);
        await _context.SaveChangesAsync();

        // 6. Followed Authors
        _context.AuthorFollowers.Add(new AuthorFollower(reader1.Id, author1.Id));
        author1.IncrementFollowers();
        _context.AuthorFollowers.Add(new AuthorFollower(reader1.Id, author4.Id));
        author4.IncrementFollowers();
        _context.AuthorFollowers.Add(new AuthorFollower(reader2.Id, author2.Id));
        author2.IncrementFollowers();

        // 7. User Libraries & Reading Progress
        var userBook1 = UserBook.Create(reader1.Id, book1.Id, UserBookStatus.Reading);
        var progress1 = ReadingProgress.Create(reader1.Id, book1.Id, book1.PageCount, 450);
        var history1 = ReadingHistory.Record(reader1.Id, book1.Id, ReadingHistoryAction.StartedBook, 0, 450);

        var userBook2 = UserBook.Create(reader1.Id, book3.Id, UserBookStatus.Completed);
        var progress2 = ReadingProgress.Create(reader1.Id, book3.Id, book3.PageCount, book3.PageCount);
        var history2 = ReadingHistory.Record(reader1.Id, book3.Id, ReadingHistoryAction.CompletedBook, 0, book3.PageCount);

        var userBook3 = UserBook.Create(reader2.Id, book2.Id, UserBookStatus.Completed);
        var progress3 = ReadingProgress.Create(reader2.Id, book2.Id, book2.PageCount, book2.PageCount);

        _context.UserBooks.AddRange(userBook1, userBook2, userBook3);
        _context.ReadingProgresses.AddRange(progress1, progress2, progress3);
        _context.ReadingHistories.AddRange(history1, history2);

        // 8. Favorites
        _context.FavoriteBooks.Add(new FavoriteBook(reader1.Id, book1.Id));
        _context.FavoriteBooks.Add(new FavoriteBook(reader2.Id, book2.Id));

        // 9. Reviews & Ratings
        var review1 = BookReview.Create(
            book1.Id,
            reader1.Id,
            5,
            "An unmatched triumph of modern epic fantasy",
            "The worldbuilding on Roshar is staggering. Kaladin and Dalinar's character arcs are inspirational and emotionally resonant.",
            ReviewStatus.Published);
        book1.ApplyNewRating(5);
        book1.IncrementReviewCount();

        var review2 = BookReview.Create(
            book2.Id,
            reader2.Id,
            5,
            "The definitive cyberpunk masterpiece",
            "Atmospheric, dark, and decades ahead of its time. Gibson created an entire aesthetic that still dominates science fiction.",
            ReviewStatus.Published);
        book2.ApplyNewRating(5);
        book2.IncrementReviewCount();

        var review3 = BookReview.Create(
            book3.Id,
            reader1.Id,
            4,
            "Mind-bending cosmic scope",
            "The cosmic sociological concepts are awe-inspiring. Hard science fiction at its absolute finest.",
            ReviewStatus.Published);
        book3.ApplyNewRating(4);
        book3.IncrementReviewCount();

        _context.BookReviews.AddRange(review1, review2, review3);

        // 10. Reading Goals
        var currentYear = DateTime.UtcNow.Year;
        var goal1 = ReadingGoal.Create(reader1.Id, currentYear, 25);
        goal1.IncrementCompleted(); // Completed book3
        var goal2 = ReadingGoal.Create(reader2.Id, currentYear, 15);
        goal2.IncrementCompleted(); // Completed book2
        _context.ReadingGoals.AddRange(goal1, goal2);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Database successfully seeded with realistic sample books, authors, genres, and user activity.");
    }
}
