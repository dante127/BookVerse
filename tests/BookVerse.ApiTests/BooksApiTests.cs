using System.Net;
using System.Net.Http.Json;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Books;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

public class BooksApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BooksApiTests(BookVerseWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBooks_ShouldReturnPaginatedPublishedBooks()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/books?page=1&pageSize=10");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSummaryDto>>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Items.Should().NotBeEmpty();
        envelope.Data.Items.Should().Contain(b => b.Title == "The Way of Kings");
    }

    [Fact]
    public async Task SearchBooks_WithKeyword_ShouldReturnMatchingResults()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/books/search?q=Neuromancer");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<BookSearchResultDto>>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Items.Should().ContainSingle(b => b.Title == "Neuromancer");
    }

    [Fact]
    public async Task GetTrendingBooks_ShouldReturnRankedBooks()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/books/trending?limit=5");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<RecommendedBookDto>>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
