using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Books;
using BookVerse.Application.Features.Library;
using BookVerse.Application.Features.Reading;
using BookVerse.Application.Features.Users;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// Phase 4 regression tests: API-02 validators, SEC-05 rate limiting and per-account
/// lockout, SEC-07 generic auth failures, SEC-08 http(s)-only URLs, API-04 status
/// codes/Location headers, and F-04 security headers + ProblemDetails for framework errors.
/// Each class gets its own host, so the per-IP "login" partition (10/min) is per class.
/// </summary>
public class Phase4ValidationApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase4ValidationApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> RegisterClientAsync(string email)
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Phase Four Reader"));
        register.EnsureSuccessStatusCode();
        var envelope = await register.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin@bookverse.io", "Admin12345!"));
        login.EnsureSuccessStatusCode();
        var envelope = await login.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    [Fact]
    public async Task Refresh_MissingToken_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "API-02: null refresh token must be rejected before hashing");
    }

    [Fact]
    public async Task CreateBook_InvalidIsbn_Returns400()
    {
        var admin = await AdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/books", new
        {
            title = "ISBN Guard Check",
            description = "A book with a malformed ISBN that must not reach the database.",
            pageCount = 100,
            isbn = "12-abc"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "API-02/SEC: CreateBookCommandValidator enforces ISBN shape");
    }

    [Fact]
    public async Task UpdateProfile_NonHttpAvatarUrl_Returns400()
    {
        var client = await RegisterClientAsync("phase4.avatar.user@bookverse.io");
        var response = await client.PutAsJsonAsync("/api/v1/users/me/profile",
            new UpdateUserProfileCommand("Phase Four Reader", null, "javascript:alert(1)", "en"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "SEC-08: URL fields accept only absolute http(s) URLs");
    }

    [Theory]
    [InlineData(1999, 5)]
    [InlineData(2026, 0)]
    public async Task SetGoal_OutOfRangeValues_Returns400(int year, int targetBooks)
    {
        var client = await RegisterClientAsync($"phase4.goal.{year}.{targetBooks}.user@bookverse.io");
        var response = await client.PostAsJsonAsync("/api/v1/reading/goals", new { year, targetBooks });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "API-02: SetReadingGoalCommandValidator bounds year and target");
    }

    [Fact]
    public async Task Responses_CarryBaselineSecurityHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/books?page=1&pageSize=1");
        response.EnsureSuccessStatusCode();
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Single().Should().Be("nosniff");
        response.Headers.TryGetValues("X-Frame-Options", out var frame).Should().BeTrue();
        frame!.Single().Should().Be("DENY");
        response.Headers.TryGetValues("Referrer-Policy", out var referrer).Should().BeTrue();
        referrer!.Single().Should().Be("no-referrer");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetailsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/definitely-not-a-route");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "F-04: framework-level status-only errors use RFC 9457 bodies");
    }

    [Fact]
    public async Task Register_LocationHeaderPointsAtCurrentUserResource()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand("phase4.location.user@bookverse.io", "StrongPass123!", "Location Check"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be("/api/v1/users/me", "API-04: Location must be a resolvable resource");
    }
}

public class Phase4StatusCodeApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase4StatusCodeApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> RegisterClientAsync(string email)
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Phase Four Reader"));
        register.EnsureSuccessStatusCode();
        var envelope = await register.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return client;
    }

    private static async Task<Guid> FindBookIdByTitleAsync(HttpClient client, string title)
    {
        var list = await client.GetAsync("/api/v1/books?page=1&pageSize=50");
        var envelope = await list.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        return envelope!.Data!.Items.Single(b => b.Title == title).Id;
    }

    [Fact]
    public async Task FavoriteBook_FirstCallCreatesSecondCallIsIdempotent()
    {
        var client = await RegisterClientAsync("phase4.favorite.user@bookverse.io");
        var bookId = await FindBookIdByTitleAsync(client, "Neuromancer");

        var first = await client.PostAsync($"/api/v1/books/{bookId}/favorite", null);
        first.StatusCode.Should().Be(HttpStatusCode.Created, "API-04: the first favorite creates a resource");

        var second = await client.PostAsync($"/api/v1/books/{bookId}/favorite", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "API-04: re-favoriting is an idempotent no-op");
    }

    [Fact]
    public async Task AddToLibrary_CreateReturns201WithLocation_ReAddReturns200()
    {
        var client = await RegisterClientAsync("phase4.library.user@bookverse.io");
        var bookId = await FindBookIdByTitleAsync(client, "Neuromancer");

        var add = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId });
        add.StatusCode.Should().Be(HttpStatusCode.Created);
        add.Headers.Location!.ToString().Should().Be($"/api/v1/library/books/{bookId}");

        var reAdd = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId, initialStatus = 2 });
        reAdd.StatusCode.Should().Be(HttpStatusCode.OK, "API-04: re-add updates status, it is not a creation");
    }

    [Fact]
    public async Task GetLibraryBook_ReturnsEntry_WhenPresent_And404_WhenAbsent()
    {
        var client = await RegisterClientAsync("phase4.libraryget.user@bookverse.io");
        var bookId = await FindBookIdByTitleAsync(client, "Neuromancer");
        var otherBookId = await FindBookIdByTitleAsync(client, "The Way of Kings");

        var add = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId });
        add.EnsureSuccessStatusCode();

        var found = await client.GetAsync($"/api/v1/library/books/{bookId}");
        found.StatusCode.Should().Be(HttpStatusCode.OK, "API-04: Location header must be resolvable");
        var envelope = await found.Content.ReadFromJsonAsync<ApiResponse<UserBookItemDto>>();
        envelope!.Data!.BookId.Should().Be(bookId);

        var missing = await client.GetAsync($"/api/v1/library/books/{otherBookId}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetGoal_CreateReturns201_UpdateReturns200()
    {
        var year = DateTime.UtcNow.Year;
        var client = await RegisterClientAsync("phase4.goalcrud.user@bookverse.io");

        var create = await client.PostAsJsonAsync("/api/v1/reading/goals", new { year, targetBooks = 5 });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<ApiResponse<ReadingGoalDto>>();
        created!.Data!.TargetBooks.Should().Be(5);

        var update = await client.PostAsJsonAsync("/api/v1/reading/goals", new { year, targetBooks = 12 });
        update.StatusCode.Should().Be(HttpStatusCode.OK, "API-04: upsert updates are not creations");
        var updated = await update.Content.ReadFromJsonAsync<ApiResponse<ReadingGoalDto>>();
        updated!.Data!.TargetBooks.Should().Be(12);
    }
}

/// <summary>
/// Lockout trigger. Consumes ~8 of this class host's 10 login permits per minute —
/// keep additional login calls out of this class.
/// </summary>
public class Phase4LockoutApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase4LockoutApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_FiveFailures_LocksAccountForSubsequentAttempts()
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand("phase4.lockout.user@bookverse.io", "StrongPass123!", "Lockout Target"));
        register.EnsureSuccessStatusCode();

        for (var i = 1; i <= LoginCommandHandler.MaxFailedLoginAttempts; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginCommand("phase4.lockout.user@bookverse.io", "WrongPass123!"));
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"failure {i} still answers with the generic 401");
        }

        var lockedWrong = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("phase4.lockout.user@bookverse.io", "WrongPass123!"));
        lockedWrong.StatusCode.Should().Be(HttpStatusCode.Forbidden, "SEC-05: after the threshold the account is locked");
        var lockedCorrect = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("phase4.lockout.user@bookverse.io", "StrongPass123!"));
        lockedCorrect.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the lockout window also blocks the correct password");
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsSameGenericMessageAsWrongPassword()
    {
        var client = _factory.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("phase4.nobody@bookverse.io", "Whatever123!"));
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unknownEnvelope = await unknown.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        var registered = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand("phase4.enumeration.user@bookverse.io", "StrongPass123!", "Enumeration Probe"));
        registered.EnsureSuccessStatusCode();
        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("phase4.enumeration.user@bookverse.io", "WrongPass123!"));
        var wrongEnvelope = await wrongPassword.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        unknownEnvelope!.Message.Should().Be(wrongEnvelope!.Message, "SEC-07: login must not reveal whether the email exists");
    }
}

/// <summary>
/// Success resets the failure counter. Consumes ~9 of this class host's 10 login
/// permits per minute — do not add login calls here.
/// </summary>
public class Phase4LockoutResetApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase4LockoutResetApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_FailuresBelowThresholdThenSuccess_ResetsCounter()
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand("phase4.reset.user@bookverse.io", "StrongPass123!", "Reset Target"));
        register.EnsureSuccessStatusCode();

        for (var i = 1; i < LoginCommandHandler.MaxFailedLoginAttempts; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginCommand("phase4.reset.user@bookverse.io", "WrongPass123!"));
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var success = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("phase4.reset.user@bookverse.io", "StrongPass123!"));
        success.StatusCode.Should().Be(HttpStatusCode.OK, "a sign-in below the threshold still succeeds");

        for (var i = 1; i < LoginCommandHandler.MaxFailedLoginAttempts; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginCommand("phase4.reset.user@bookverse.io", "WrongPass123!"));
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "SEC-05: failures after a successful login restart from 1, so 403 must not appear yet");
        }
    }
}

public class Phase4RateLimitApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase4RateLimitApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_BurstBeyondWindow_Returns429WithEnvelopeAndRetryAfter()
    {
        var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // "login" policy: 10 permits/min/IP, QueueLimit 0. Thirteen fast requests with
        // unknown emails cannot hit the 5-failure account lockout, so any non-401 is the limiter.
        for (var i = 0; i < 13; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginCommand($"phase4.burst{i}@bookverse.io", "Whatever123!"));
            statuses.Add(attempt.StatusCode);

            if (statuses[^1] == HttpStatusCode.TooManyRequests)
            {
                attempt.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
                int.Parse(retryAfter!.Single()).Should().BeGreaterThan(0);
                var envelope = await attempt.Content.ReadFromJsonAsync<ApiResponse>();
                envelope!.Success.Should().BeFalse();
                envelope.Message.Should().Be("Too many requests. Please try again later.");
                break;
            }
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "SEC-05: exceeding the fixed window must yield 429");
        statuses[0].Should().NotBe(HttpStatusCode.TooManyRequests);
    }
}
