using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Api.Controllers.v1;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Books;
using BookVerse.Application.Features.Library;
using BookVerse.Application.Features.Reading;
using BookVerse.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// Phase 3 regression tests (BL-04/BL-05/BL-07): completion is coordinated in one
/// place, so completing, rewinding and re-completing a book counts the yearly goal
/// exactly once, status changes sync progress, and library removal leaves no orphans.
/// </summary>
public class Phase3BusinessLogicApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public Phase3BusinessLogicApiTests(BookVerseWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> RegisterClientAsync(string email)
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterCommand(email, "StrongPass123!", "Phase Three Reader"));
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

    private static async Task<int> GetCompletedBooksAsync(HttpClient client, int year)
    {
        var response = await client.GetAsync("/api/v1/reading/goals");
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<ReadingGoalDto>>>();
        return envelope!.Data!.Single(g => g.Year == year).CompletedBooks;
    }

    private static async Task SetGoalAsync(HttpClient client, int year)
    {
        var response = await client.PostAsJsonAsync("/api/v1/reading/goals", new { year, targetBooks = 5 });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SetProgressAsync(HttpClient client, Guid bookId, int page)
        => await client.PostAsJsonAsync($"/api/v1/books/{bookId}/progress", new { currentPage = page });

    [Fact]
    public async Task ReadingGoal_CompleteRewindRecomplete_CountsExactlyOnce()
    {
        var year = DateTime.UtcNow.Year;
        var client = await RegisterClientAsync("phase3.goal.user@bookverse.io");
        await SetGoalAsync(client, year);

        var bookId = await FindBookIdByTitleAsync(client, "Neuromancer");
        var detail = await client.GetFromJsonAsync<ApiResponse<BookDetailDto>>($"/api/v1/books/{bookId}");
        var pageCount = detail!.Data!.PageCount;

        var add = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId });
        add.EnsureSuccessStatusCode();

        // Complete, then complete again — the second save must not double-count.
        (await SetProgressAsync(client, bookId, pageCount)).EnsureSuccessStatusCode();
        (await GetCompletedBooksAsync(client, year)).Should().Be(1, "first completion counts once");
        (await SetProgressAsync(client, bookId, pageCount)).EnsureSuccessStatusCode();
        (await GetCompletedBooksAsync(client, year)).Should().Be(1, "re-sending the final page is idempotent");

        // BL-04: rewinding past the last page decrements, re-completing increments again.
        (await SetProgressAsync(client, bookId, pageCount - 10)).EnsureSuccessStatusCode();
        (await GetCompletedBooksAsync(client, year)).Should().Be(0, "a rewound book frees its goal slot");

        (await SetProgressAsync(client, bookId, pageCount)).EnsureSuccessStatusCode();
        (await GetCompletedBooksAsync(client, year)).Should().Be(1, "re-completion counts once, not twice");
    }

    [Fact]
    public async Task UpdateUserBookStatus_ToCompleted_SyncsProgressAndGoal()
    {
        var year = DateTime.UtcNow.Year;
        var client = await RegisterClientAsync("phase3.status.user@bookverse.io");
        await SetGoalAsync(client, year);

        var bookId = await FindBookIdByTitleAsync(client, "The Way of Kings");
        var detail = await client.GetFromJsonAsync<ApiResponse<BookDetailDto>>($"/api/v1/books/{bookId}");
        var pageCount = detail!.Data!.PageCount;

        var add = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId });
        add.EnsureSuccessStatusCode();

        // BL-04: flipping the library status is now a first-class completion path.
        var setStatus = await client.PutAsJsonAsync($"/api/v1/library/books/{bookId}",
            new UpdateStatusRequest(UserBookStatus.Completed));
        setStatus.EnsureSuccessStatusCode();

        (await GetCompletedBooksAsync(client, year)).Should().Be(1);

        var library = await client.GetFromJsonAsync<ApiResponse<PagedResult<UserBookItemDto>>>("/api/v1/library?page=1&pageSize=10");
        var entry = library!.Data!.Items.Single(i => i.BookId == bookId);
        entry.Status.Should().Be(UserBookStatus.Completed);
        entry.CurrentPage.Should().Be(pageCount);
        entry.Percentage.Should().Be(100.00m);
    }

    [Fact]
    public async Task RemoveFromLibrary_CleansUpProgressHistoryAndGoal()
    {
        var year = DateTime.UtcNow.Year;
        var client = await RegisterClientAsync("phase3.remove.user@bookverse.io");
        await SetGoalAsync(client, year);

        var bookId = await FindBookIdByTitleAsync(client, "Neuromancer");
        var detail = await client.GetFromJsonAsync<ApiResponse<BookDetailDto>>($"/api/v1/books/{bookId}");
        var pageCount = detail!.Data!.PageCount;

        var add = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId });
        add.EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/v1/books/{bookId}/favorite", null)).EnsureSuccessStatusCode();
        (await SetProgressAsync(client, bookId, pageCount)).EnsureSuccessStatusCode();
        (await GetCompletedBooksAsync(client, year)).Should().Be(1);

        var remove = await client.DeleteAsync($"/api/v1/library/books/{bookId}");
        remove.EnsureSuccessStatusCode();

        (await GetCompletedBooksAsync(client, year)).Should().Be(0, "removing a completed book releases its goal slot");

        // BL-07: no orphaned reading history rows survive the removal.
        var history = await client.GetFromJsonAsync<ApiResponse<PagedResult<ReadingHistoryItemDto>>>("/api/v1/reading/history?page=1&pageSize=50");
        history!.Data!.Items.Should().NotContain(h => h.BookId == bookId);

        var library = await client.GetFromJsonAsync<ApiResponse<PagedResult<UserBookItemDto>>>("/api/v1/library?page=1&pageSize=50");
        library!.Data!.Items.Should().NotContain(i => i.BookId == bookId);

        // Re-adding starts from a clean slate.
        var reAdd = await client.PostAsJsonAsync("/api/v1/library/books", new { bookId, initialStatus = (int)UserBookStatus.WantToRead });
        reAdd.EnsureSuccessStatusCode();
        var library2 = await client.GetFromJsonAsync<ApiResponse<PagedResult<UserBookItemDto>>>("/api/v1/library?page=1&pageSize=50");
        var entry = library2!.Data!.Items.Single(i => i.BookId == bookId);
        entry.CurrentPage.Should().Be(0, "progress was deleted with the library entry");
    }
}
