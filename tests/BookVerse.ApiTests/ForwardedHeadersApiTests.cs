using System.Net.Http.Json;
using BookVerse.Application.Features.Auth;
using BookVerse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// PROD-1: behind a reverse proxy the socket peer is the proxy, so X-Forwarded-For must be
/// resolved into Connection.RemoteIpAddress before the login handler captures the caller IP.
/// If the middleware were missing or untrusted, the stored refresh-token IP would be the
/// loopback test client, not the forwarded value.
/// </summary>
public class ForwardedHeadersApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public ForwardedHeadersApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_WithForwardedFor_PersistsRealClientIp()
    {
        const string forwardedIp = "203.0.113.77";

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", forwardedIp);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginCommand("admin@bookverse.io", "Admin12345!"));
        login.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var storedIp = await db.RefreshTokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.CreatedByIp)
            .FirstAsync();

        storedIp.Should().Be(forwardedIp,
            "X-Forwarded-For from the trusted loopback test peer must overwrite the socket IP before the handler reads it");
    }
}
