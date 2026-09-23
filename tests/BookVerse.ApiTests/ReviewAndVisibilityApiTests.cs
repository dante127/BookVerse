using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Api.Controllers.v1;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Books;
using BookVerse.Application.Features.Reviews;
using BookVerse.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// Phase 0 regression tests: anonymous book visibility (SEC-03) and
/// review eligibility / re-moderation (BUS-01, BUS-02).
/// </summary>
public class ReviewAndVisibilityApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private const string DraftTitle = "The Knights of the Draft (Unpublished Manuscript)";
    private readonly BookVerseWebApplicationFactory _factory;

    public ReviewAndVisibilityApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

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

    private static async Task<Guid> FindBookIdByTitleAsync(HttpClient client, string title)
    {
        var list = await client.GetAsync("/api/v1/books?status=&page=1&pageSize=50");
        var envelope = await list.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        return envelope!.Data!.Items.Single(b => b.Title == title).Id;
    }

    [Fact]
    public async Task AnonymousBooksList_IgnoresStatusBypass_AndNeverReturnsDrafts()
    {
        // Act: send an empty status to reproduce the old bypass (`?status=` removed the filter entirely)
        var client = AnonClient();
        var response = await client.GetAsync("/api/v1/books?status=&page=1&pageSize=50");
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        envelope!.Data!.Items.Should().NotBeEmpty();
        envelope.Data.Items.Should().OnlyContain(b => b.Status == BookStatus.Published);
        envelope.Data.Items.Should().NotContain(b => b.Title == DraftTitle);
    }

    [Fact]
    public async Task AnonymousBookById_DraftReturns404_ButAdminSeesIt()
    {
        // Arrange: locate the draft via an admin client (Admin may list Draft status)
        var admin = await UserClientAsync("admin@bookverse.io", "Admin12345!");
        var adminList = await admin.GetAsync("/api/v1/books?status=Draft&pageSize=50");
        var adminEnvelope = await adminList.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        var draft = adminEnvelope!.Data!.Items.SingleOrDefault(b => b.Title == DraftTitle);
        draft.Should().NotBeNull("the seeder must include a Draft book for this test");

        // Act: anonymous detail lookup of the same id
        var anonResponse = await AnonClient().GetAsync($"/api/v1/books/{draft!.Id}");

        // Assert
        anonResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // admin can still see it
        var adminDetail = await admin.GetAsync($"/api/v1/books/{draft.Id}");
        adminDetail.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Review_WithoutBookInLibrary_Returns403()
    {
        // Arrange: fresh user who owns no books
        var client = AnonClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand("no.library.reader@bookverse.io", "StrongPass123!", "No Library"));
        register.EnsureSuccessStatusCode();
        var registerEnvelope = await register.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerEnvelope!.Data!.AccessToken);

        var neuromancerId = await FindBookIdByTitleAsync(client, "Neuromancer");

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/books/{neuromancerId}/reviews",
            new CreateReviewRequest(5, "drive-by review", "I have never read this book but five stars anyway."));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("library");
    }

    [Fact]
    public async Task ReviewEdit_ReModeratesContent_AndRestoresOnCleanResubmission()
    {
        // Arrange: Elena owns "The Way of Kings" in her seeded library and has a published review.
        var elena = await UserClientAsync("elena.rostova@bookverse.io", "Reader12345!");
        var bookId = await FindBookIdByTitleAsync(elena, "The Way of Kings");

        // Act 1: edit the review with spam content
        var spamResponse = await elena.PostAsJsonAsync($"/api/v1/books/{bookId}/reviews",
            new CreateReviewRequest(5, "Amazing fantasy", "Great book. Buy fake followers at our casino, click here now!"));
        var spamEnvelope = await spamResponse.Content.ReadFromJsonAsync<ApiResponse<BookReviewDto>>();

        // Assert 1: demoted to Pending and removed from rating aggregates
        spamResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        spamEnvelope!.Data!.Status.Should().Be(ReviewStatus.Pending);

        var afterDemote = await elena.GetAsync($"/api/v1/books/{bookId}");
        var afterDemoteEnvelope = await afterDemote.Content.ReadFromJsonAsync<ApiResponse<BookDetailDto>>();
        afterDemoteEnvelope!.Data!.RatingsCount.Should().Be(0);
        afterDemoteEnvelope.Data.AverageRating.Should().Be(0m);

        // Act 2: resubmit clean content
        var cleanResponse = await elena.PostAsJsonAsync($"/api/v1/books/{bookId}/reviews",
            new CreateReviewRequest(5, "Amazing fantasy", "The worldbuilding on Roshar is staggering and inspiring."));
        var cleanEnvelope = await cleanResponse.Content.ReadFromJsonAsync<ApiResponse<BookReviewDto>>();

        // Assert 2: re-published and rating restored
        cleanEnvelope!.Data!.Status.Should().Be(ReviewStatus.Published);

        var afterPromote = await elena.GetAsync($"/api/v1/books/{bookId}");
        var afterPromoteEnvelope = await afterPromote.Content.ReadFromJsonAsync<ApiResponse<BookDetailDto>>();
        afterPromoteEnvelope!.Data!.RatingsCount.Should().Be(1);
        afterPromoteEnvelope.Data.AverageRating.Should().Be(5m);
    }
}
