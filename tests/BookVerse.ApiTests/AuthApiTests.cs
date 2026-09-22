using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BookVerse.Application.Common.Models;
using BookVerse.Application.Features.Auth;
using BookVerse.Application.Features.Users;
using FluentAssertions;
using Xunit;

namespace BookVerse.ApiTests;

public class AuthApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthApiTests(BookVerseWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithValidData_ShouldReturn201Created()
    {
        // Arrange
        var request = new RegisterCommand("new.reader@bookverse.io", "StrongPass123!", "New Reader");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        envelope.Data!.Email.Should().Be("new.reader@bookverse.io");
        envelope.Data.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange: Use seeded account
        var request = new LoginCommand("admin@bookverse.io", "Admin12345!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.AccessToken.Should().NotBeNullOrEmpty();
        envelope.Data.RefreshToken.Should().NotBeNullOrEmpty();
        envelope.Data.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldReturn401Unauthorized()
    {
        // Arrange
        var request = new LoginCommand("admin@bookverse.io", "WrongPassword!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidBearerToken_ShouldReturnUserProfile()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("elena.rostova@bookverse.io", "Reader12345!"));
        var authResult = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        var token = authResult!.Data!.AccessToken;

        // 2. Call /api/v1/users/me with Bearer token
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<UserProfileDto>>();
        envelope!.Success.Should().BeTrue();
        envelope.Data!.DisplayName.Should().Be("Elena Rostova");
    }

    [Fact]
    public async Task GetMe_WithoutToken_ShouldReturn401Unauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/users/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
