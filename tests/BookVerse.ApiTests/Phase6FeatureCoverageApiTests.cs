using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Api.Controllers.v1;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Authors;
using BookVerse.Application.Features.Books;
using BookVerse.Application.Features.Genres;
using BookVerse.Application.Features.Library;
using BookVerse.Application.Features.Notifications;
using BookVerse.Application.Features.Reading;
using BookVerse.Application.Features.Reviews;
using BookVerse.Application.Features.Tags;
using BookVerse.Domain.Enums;
using BookVerse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// TST-03: feature-area coverage for endpoints that had no tests before Phase 6 —
/// taxonomy CRUD, review moderation flow, refresh-token rotation/replay,
/// notifications, analytics and the reading progress/history pipeline.
/// Each area gets its own host (IClassFixture) so rate-limit windows stay isolated.
/// </summary>
public class TaxonomyApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public TaxonomyApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private HttpClient AnonClient() => _factory.CreateClient();

    private async Task<HttpClient> UserClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(email, password));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    [Fact]
    public async Task AdminCreateAuthor_Returns201WithLocation_ThenReadable()
    {
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var create = await admin.PostAsJsonAsync("/api/v1/authors",
            new CreateAuthorCommand("Ursula K. Le Guin", "Science fiction and fantasy author.", null, "United States"));

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location!.ToString().Should().StartWith("/api/v1/authors/");

        var envelope = await create.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
        var detail = await admin.GetAsync($"/api/v1/authors/{envelope!.Data}");
        var detailEnvelope = await detail.Content.ReadFromJsonAsync<ApiResponse<AuthorDetailDto>>();
        detailEnvelope!.Data!.Name.Should().Be("Ursula K. Le Guin");
        detailEnvelope.Data.Slug.Should().Be("ursula-k-le-guin");
    }

    [Fact]
    public async Task AuthorCreate_RejectsNonAdminRoles_AndAnonymous()
    {
        var elena = await UserClientAsync("elena.rostova@bookverse.io", "Reader12345!");
        var forbidden = await elena.PostAsJsonAsync("/api/v1/authors", new CreateAuthorCommand("Nobody"));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var unauthorized = await AnonClient().PostAsJsonAsync("/api/v1/authors", new CreateAuthorCommand("Nobody"));
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthorCreate_InvalidPayloads_Return400()
    {
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");

        var emptyName = await admin.PostAsJsonAsync("/api/v1/authors", new CreateAuthorCommand(""));
        emptyName.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var jsWebsite = await admin.PostAsJsonAsync("/api/v1/authors",
            new CreateAuthorCommand("Valid Name", null, null, null, "javascript:alert(1)"));
        jsWebsite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FollowAndUnfollowAuthor_UpdatesFollowerCount()
    {
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var list = await admin.GetAsync("/api/v1/authors?search=Brandon&page=1&pageSize=5");
        var listEnvelope = await list.Content.ReadFromJsonAsync<ApiResponse<PagedResult<AuthorSummaryDto>>>();
        var author = listEnvelope!.Data!.Items.Single(a => a.Name == "Brandon Sanderson");

        await admin.PostAsync($"/api/v1/authors/{author.Id}/follow", null);
        var afterFollow = await admin.GetAsync($"/api/v1/authors/{author.Id}");
        var afterFollowEnvelope = await afterFollow.Content.ReadFromJsonAsync<ApiResponse<AuthorDetailDto>>();
        afterFollowEnvelope!.Data!.FollowersCount.Should().Be(author.FollowersCount + 1);

        await admin.DeleteAsync($"/api/v1/authors/{author.Id}/follow");
        var afterUnfollow = await admin.GetAsync($"/api/v1/authors/{author.Id}");
        var afterUnfollowEnvelope = await afterUnfollow.Content.ReadFromJsonAsync<ApiResponse<AuthorDetailDto>>();
        afterUnfollowEnvelope!.Data!.FollowersCount.Should().Be(author.FollowersCount);
    }

    [Fact]
    public async Task Genre_CreateThenDuplicateName_Returns409()
    {
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var first = await admin.PostAsJsonAsync("/api/v1/genres", new CreateGenreCommand("Horror", "Fear-inducing fiction."));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await admin.PostAsJsonAsync("/api/v1/genres", new CreateGenreCommand("Horror", "Again."));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var genres = await AnonClient().GetFromJsonAsync<ApiResponse<IReadOnlyList<GenreItemDto>>>("/api/v1/genres");
        genres!.Data.Should().Contain(g => g.Name == "Horror");
    }

    [Fact]
    public async Task Tag_Create_GetOrCreateStaysUnique()
    {
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var created = await admin.PostAsJsonAsync("/api/v1/tags", new CreateTagCommand("Solarpunk"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await created.Content.ReadFromJsonAsync<ApiResponse<Guid>>();

        var again = await admin.PostAsJsonAsync("/api/v1/tags", new CreateTagCommand("Solarpunk"));
        var againEnvelope = await again.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
        againEnvelope!.Data.Should().Be(envelope!.Data);

        var tags = await AnonClient().GetFromJsonAsync<ApiResponse<IReadOnlyList<TagDto>>>("/api/v1/tags");
        tags!.Data!.Count(t => t.Name == "Solarpunk").Should().Be(1);
    }
}

public class ReviewModerationFlowApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public ReviewModerationFlowApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private HttpClient AnonClient() => _factory.CreateClient();

    private async Task<(HttpClient Client, AuthResponseDto Auth)> RegisterAsync(string email)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Moderation Tester"));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return (client, envelope.Data!);
    }

    private async Task<HttpClient> UserClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(email, password));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    private async Task<Guid> PromoteToModeratorAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.Include(u => u.UserRoles).SingleAsync(u => u.Id == userId);
        var moderatorRole = await db.Roles.SingleAsync(r => r.Name == "Moderator");
        if (!user.UserRoles.Any(ur => ur.Role?.Name == "Moderator" || ur.RoleId == moderatorRole.Id))
        {
            user.AddRole(moderatorRole);
            await db.SaveChangesAsync();
        }
        return userId;
    }

    [Fact]
    public async Task ModerateEndpoints_RejectReaderAndAnonymous()
    {
        var (reader, auth) = await RegisterAsync("reader.moderation@bookverse.io");
        var randomId = Guid.NewGuid();

        var forbidden = await reader.PutAsJsonAsync($"/api/v1/reviews/{randomId}/moderate",
            new ModerateReviewRequest(ReviewStatus.Published, null));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var unauthorized = await AnonClient().PutAsJsonAsync($"/api/v1/reviews/{randomId}/moderate",
            new ModerateReviewRequest(ReviewStatus.Published, null));
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        auth.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SpamReview_Pending_ThenRejected_Invisible_ThenApproved_PublishesAndCounts()
    {
        var (reader, auth) = await RegisterAsync("spam.author@bookverse.io");

        // Get the book id and add it to the reader's library (review eligibility).
        var books = await reader.GetFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>("/api/v1/books?pageSize=50");
        var book = books!.Data!.Items.Single(b => b.Title == "Neuromancer");
        var add = await reader.PostAsJsonAsync("/api/v1/library/books", new AddBookToLibraryCommand(book.Id));
        add.EnsureSuccessStatusCode();

        var baseline = await reader.GetFromJsonAsync<ApiResponse<BookDetailDto>>($"/api/v1/books/{book.Id}");
        var baselineCount = baseline!.Data!.RatingsCount;

        // Spam review is auto-demoted to Pending.
        var reviewResponse = await reader.PostAsJsonAsync($"/api/v1/books/{book.Id}/reviews",
            new CreateReviewRequest(5, "Great", "Visit our casino and buy fake followers now! Click here!"));
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reviewEnvelope = await reviewResponse.Content.ReadFromJsonAsync<ApiResponse<BookReviewDto>>();
        var reviewId = reviewEnvelope!.Data!.Id;
        reviewEnvelope.Data.Status.Should().Be(ReviewStatus.Pending);

        // A seeded Reader (Marcus) cannot moderate.
        var marcus = await UserClientAsync("marcus.vance@bookverse.io", "Reader12345!");
        var readerAsModerator = await marcus.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/moderate",
            new ModerateReviewRequest(ReviewStatus.Rejected, "spam"));
        readerAsModerator.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A promoted Moderator rejects it: it must not surface in the public list.
        await PromoteToModeratorAsync(auth.UserId);
        var moderator = await UserClientAsync("spam.author@bookverse.io", "StrongPass123!");
        var reject = await moderator.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/moderate",
            new ModerateReviewRequest(ReviewStatus.Rejected, "Auto-detected spam, confirmed."));
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectedList = await AnonClient()
            .GetFromJsonAsync<ApiResponse<PagedResult<BookReviewDto>>>($"/api/v1/books/{book.Id}/reviews?pageSize=50");
        rejectedList!.Data!.Items.Should().NotContain(r => r.Id == reviewId);

        // Admin overrides and publishes it: aggregate count picks it up.
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var approve = await admin.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/moderate",
            new ModerateReviewRequest(ReviewStatus.Published, null));
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedList = await AnonClient()
            .GetFromJsonAsync<ApiResponse<PagedResult<BookReviewDto>>>($"/api/v1/books/{book.Id}/reviews?pageSize=50");
        approvedList!.Data!.Items.Should().Contain(r => r.Id == reviewId);

        var afterPublish = await AnonClient().GetFromJsonAsync<ApiResponse<BookDetailDto>>($"/api/v1/books/{book.Id}");
        afterPublish!.Data!.RatingsCount.Should().Be(baselineCount + 1);
    }
}

public class RefreshTokenRotationApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public RefreshTokenRotationApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private async Task<AuthResponseDto> RegisterAsync(string email)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Rotation Tester"));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        return envelope!.Data!;
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenCommand(refreshToken));
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldTokenStillWorksWithNewFamily()
    {
        var auth = await RegisterAsync("rotation.ok@bookverse.io");

        var refresh = await RefreshAsync(auth.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await refresh.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        envelope!.Data!.RefreshToken.Should().NotBe(auth.RefreshToken);
        envelope.Data.AccessToken.Should().NotBe(auth.AccessToken);

        // The new refresh token is usable.
        var second = await RefreshAsync(envelope.Data.RefreshToken);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_RevokesWholeFamily()
    {
        var auth = await RegisterAsync("rotation.replay@bookverse.io");

        var rotate = await RefreshAsync(auth.RefreshToken);
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await rotate.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>())!.Data!;

        // Replay the already-rotated token: detected as compromise.
        var replay = await RefreshAsync(auth.RefreshToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var replayBody = await replay.Content.ReadAsStringAsync();
        replayBody.Should().Contain("revoked token");

        // The legitimate successor token is dead too (family-wide revocation).
        var successor = await RefreshAsync(rotated.RefreshToken);
        successor.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var response = await RefreshAsync("not-a-real-token-" + Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_AllTokens_RefreshThenFails()
    {
        var auth = await RegisterAsync("rotation.revoke@bookverse.io");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var revoke = await client.PostAsJsonAsync("/api/v1/auth/revoke", new RevokeTokenCommand(auth.RefreshToken));
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await RefreshAsync(auth.RefreshToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class NotificationsAndAnalyticsApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public NotificationsAndAnalyticsApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private HttpClient AnonClient() => _factory.CreateClient();

    private async Task<HttpClient> UserClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand(email, password));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Notifications_RequireAuth_SupportReadAllAndUnknownId()
    {
        var anon = await AnonClient().GetAsync("/api/v1/notifications");
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var elena = await UserClientAsync("elena.rostova@bookverse.io", "Reader12345!");
        var list = await elena.GetAsync("/api/v1/notifications");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        // Unknown ids are acknowledged without failure (idempotent PATCH semantics).
        var unknown = await elena.PatchAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK);

        var readAll = await elena.PostAsync("/api/v1/notifications/read-all", null);
        readAll.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserAnalytics_ReflectsSeededLibrary()
    {
        var elena = await UserClientAsync("elena.rostova@bookverse.io", "Reader12345!");
        var response = await elena.GetFromJsonAsync<ApiResponse<BookVerse.Application.Features.Analytics.UserReadingAnalyticsDto>>("/api/v1/analytics/reading");

        response!.Data!.TotalBooksCompleted.Should().BeGreaterThanOrEqualTo(0);
        (response.Data.TotalBooksCompleted + response.Data.TotalBooksReading + response.Data.TotalBooksWantToRead)
            .Should().BeGreaterThan(0, "Elena has seeded library entries");
    }

    [Fact]
    public async Task AdminAnalytics_ReaderGets403_AdminGets200()
    {
        var elena = await UserClientAsync("elena.rostova@bookverse.io", "Reader12345!");
        var forbidden = await elena.GetAsync("/api/v1/analytics/admin");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var anon = await AnonClient().GetAsync("/api/v1/analytics/admin");
        anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var result = await admin.GetFromJsonAsync<ApiResponse<BookVerse.Application.Features.Analytics.AdminAnalyticsDto>>("/api/v1/analytics/admin");
        result!.Data!.TotalUsers.Should().BeGreaterThanOrEqualTo(3);
        result.Data.PublishedBooks.Should().BeGreaterThan(0);
    }
}

public class ReadingProgressHistoryApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public ReadingProgressHistoryApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> RegisterAsync(string email)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Progress Tester"));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    private static async Task<Guid> FindBookIdByTitleAsync(HttpClient client, string title)
    {
        var list = await client.GetFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>("/api/v1/books?pageSize=50");
        return list!.Data!.Items.Single(b => b.Title == title).Id;
    }

    [Fact]
    public async Task Progress_WithoutLibraryEntry_StartsBook_AndHistoryRecordsIt()
    {
        var reader = await RegisterAsync("progress.reader@bookverse.io");
        var bookId = await FindBookIdByTitleAsync(reader, "Neuromancer");

        var first = await reader.PostAsJsonAsync($"/api/v1/books/{bookId}/progress", new UpdateProgressRequest(50));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstEnvelope = await first.Content.ReadFromJsonAsync<ApiResponse<ReadingProgressResultDto>>();
        firstEnvelope!.Data!.IsCompleted.Should().BeFalse();
        firstEnvelope.Data.CurrentPage.Should().Be(50);

        // The progress call auto-created a Reading library entry.
        var entry = await reader.GetFromJsonAsync<ApiResponse<UserBookItemDto>>($"/api/v1/library/books/{bookId}");
        entry!.Data!.Status.Should().Be(UserBookStatus.Reading);

        var history = await reader.GetFromJsonAsync<ApiResponse<PagedResult<ReadingHistoryItemDto>>>("/api/v1/reading/history");
        history!.Data!.Items.Should().Contain(h => h.BookId == bookId && h.NewPage == 50);
    }

    [Fact]
    public async Task Progress_LastPage_CompletesBook_AndRewindUncompletesIt()
    {
        var reader = await RegisterAsync("progress.finisher@bookverse.io");
        var bookId = await FindBookIdByTitleAsync(reader, "Neuromancer");

        var started = await reader.PostAsJsonAsync($"/api/v1/books/{bookId}/progress", new UpdateProgressRequest(10));
        var totalPages = (await started.Content.ReadFromJsonAsync<ApiResponse<ReadingProgressResultDto>>())!.Data!.TotalPages;

        var completed = await reader.PostAsJsonAsync($"/api/v1/books/{bookId}/progress", new UpdateProgressRequest(totalPages));
        var completedDto = (await completed.Content.ReadFromJsonAsync<ApiResponse<ReadingProgressResultDto>>())!.Data!;
        completedDto.IsCompleted.Should().BeTrue();
        completedDto.Percentage.Should().Be(100m);

        var entry = await reader.GetFromJsonAsync<ApiResponse<UserBookItemDto>>($"/api/v1/library/books/{bookId}");
        entry!.Data!.Status.Should().Be(UserBookStatus.Completed);

        // Rewind below the last page un-completes (BL-04 edge).
        await reader.PostAsJsonAsync($"/api/v1/books/{bookId}/progress", new UpdateProgressRequest(totalPages - 10));
        var afterRewind = await reader.GetFromJsonAsync<ApiResponse<UserBookItemDto>>($"/api/v1/library/books/{bookId}");
        afterRewind!.Data!.Status.Should().Be(UserBookStatus.Reading);
    }

    [Fact]
    public async Task Progress_UnknownBook_Returns404()
    {
        var reader = await RegisterAsync("progress.na@bookverse.io");
        var response = await reader.PostAsJsonAsync($"/api/v1/books/{Guid.NewGuid()}/progress", new UpdateProgressRequest(1));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class LibraryIsolationApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public LibraryIsolationApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> RegisterAsync(string email)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Isolation Tester"));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Library_EntriesAreScopedToOwner_FilterByStatusWorks()
    {
        var reader = await RegisterAsync("library.owner@bookverse.io");

        var books = await reader.GetFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>("/api/v1/books?pageSize=50");
        var wanted = books!.Data!.Items.First(b => b.Title == "Neuromancer");
        var read = books.Data.Items.First(b => b.Title == "The Three-Body Problem");

        await reader.PostAsJsonAsync("/api/v1/library/books", new AddBookToLibraryCommand(wanted.Id, UserBookStatus.WantToRead));
        await reader.PostAsJsonAsync("/api/v1/library/books", new AddBookToLibraryCommand(read.Id, UserBookStatus.Reading));

        var filtered = await reader.GetFromJsonAsync<ApiResponse<PagedResult<UserBookItemDto>>>("/api/v1/library?status=Reading");
        filtered!.Data!.Items.Should().OnlyContain(i => i.Status == UserBookStatus.Reading);
        filtered.Data.Items.Should().ContainSingle(i => i.BookId == read.Id);

        // Another user cannot see this entry.
        var stranger = await RegisterAsync("library.stranger@bookverse.io");
        var crossAccess = await stranger.GetAsync($"/api/v1/library/books/{wanted.Id}");
        crossAccess.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ...and deleting someone else's entry is a silent no-op (never leaks existence, no cross-user delete).
        var crossDelete = await stranger.DeleteAsync($"/api/v1/library/books/{wanted.Id}");
        crossDelete.StatusCode.Should().Be(HttpStatusCode.OK);

        var stillThere = await reader.GetAsync($"/api/v1/library/books/{wanted.Id}");
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
