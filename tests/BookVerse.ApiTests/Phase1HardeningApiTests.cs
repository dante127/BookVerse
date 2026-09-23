using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Books;
using BookVerse.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

public class Phase1HardeningApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Phase1HardeningApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("admin@bookverse.io", "Admin12345!"));
        login.EnsureSuccessStatusCode();
        var envelope = await login.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", envelope!.Data!.AccessToken);
        return adminClient;
    }

    [Fact]
    public async Task GetBooks_OversizedPageSize_ShouldBeClampedTo50()
    {
        var response = await _client.GetAsync("/api/v1/books?page=1&pageSize=1000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        envelope!.Data!.PageSize.Should().Be(50);
        envelope.Data.Items.Count.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task GetBooks_NegativePageAndPageSize_ShouldBeNormalized()
    {
        var response = await _client.GetAsync("/api/v1/books?page=-5&pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        envelope!.Data!.Page.Should().Be(1);
        envelope.Data.PageSize.Should().Be(Pagination.DefaultPageSize);
    }

    [Fact]
    public async Task SearchBooks_Paging_ShouldBeStableAndDisjoint()
    {
        var first1 = await _client.GetAsync("/api/v1/books/search?page=1&pageSize=2");
        var first2 = await _client.GetAsync("/api/v1/books/search?page=2&pageSize=2");
        var second1 = await _client.GetAsync("/api/v1/books/search?page=1&pageSize=2");

        first1.EnsureSuccessStatusCode();
        first2.EnsureSuccessStatusCode();
        second1.EnsureSuccessStatusCode();

        var env1 = await first1.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSearchResultDto>>>();
        var env2 = await first2.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSearchResultDto>>>();
        var envRepeat = await second1.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSearchResultDto>>>();

        var page1 = env1!.Data!.Items.Select(b => b.Id).ToList();
        var page2 = env2!.Data!.Items.Select(b => b.Id).ToList();
        var page1Repeat = envRepeat!.Data!.Items.Select(b => b.Id).ToList();

        page1.Should().NotBeEmpty();
        page1Repeat.Should().Equal(page1, "repeating the same page request must return identical ordering");
        page1.Intersect(page2).Should().BeEmpty("pages must not overlap");

        var total = env1.Data.TotalCount;
        (page1.Count + page2.Count).Should().Be(Math.Min(total, 4));
    }

    [Fact]
    public async Task AddEdition_DuplicateIsbn_ShouldReturn409Not500()
    {
        var adminClient = await CreateAdminClientAsync();

        var list = await adminClient.GetAsync("/api/v1/books?pageSize=50");
        var listEnvelope = await list.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        var books = listEnvelope!.Data!.Items.ToList();
        books.Count.Should().BeGreaterThanOrEqualTo(2);

        const string isbn = "9789090909090";

        var first = await adminClient.PostAsJsonAsync($"/api/v1/books/{books[0].Id}/editions",
            new AddBookEditionCommand(books[0].Id, isbn, BookEditionFormat.Ebook));
        first.StatusCode.Should().Be(HttpStatusCode.Created,
            "the first edition with a fresh ISBN must succeed");

        var second = await adminClient.PostAsJsonAsync($"/api/v1/books/{books[1].Id}/editions",
            new AddBookEditionCommand(books[1].Id, isbn, BookEditionFormat.Hardcover));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadAsStringAsync();
        body.Should().Contain("unique value", because: "the DB unique-constraint violation must surface as a 409 envelope, not a 500");
    }
}
