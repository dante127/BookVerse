using BookVerse.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// L-02: the correlation middleware must echo sane client ids but never accept
/// unbounded or junk values for logs and response headers.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private const string Header = "X-Correlation-ID";

    private static async Task<string> RunAsync(string? incoming)
    {
        var context = new DefaultHttpContext();
        if (incoming != null)
        {
            context.Request.Headers[Header] = incoming;
        }

        string? seen = null;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            seen = context.Response.Headers[Header].FirstOrDefault();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return seen!;
    }

    [Theory]
    [InlineData("request-abc-123")]
    [InlineData("TRACE_ID_42")]
    [InlineData("0f8e2a1c4b6d8e0f")]
    public async Task ValidClientIds_AreEchoed(string incoming)
    {
        (await RunAsync(incoming)).Should().Be(incoming);
    }

    [Theory]
    [InlineData("short")]                       // under 8 chars
    [InlineData("bad id with spaces and junk!")]// unsafe characters
    [InlineData("a-very-long-correlation-id-that-exceeds-the-sixty-four-character-budget-completely")]
    public async Task JunkClientIds_AreReplacedWithFreshGuid(string incoming)
    {
        var result = await RunAsync(incoming);

        result.Should().NotBe(incoming);
        result.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task MissingHeader_GeneratesCorrelationId()
    {
        var result = await RunAsync(null);

        result.Should().NotBeNullOrEmpty();
        result.Should().MatchRegex("^[0-9a-f]{32}$");
    }
}

/// <summary>
/// L-03: /health (liveness) and /health/ready (readiness) must both answer with the
/// bare status word only — never check names or provider details.
/// </summary>
public class HealthEndpointApiTests : IClassFixture<BookVerseWebApplicationFactory>
{
    private readonly BookVerseWebApplicationFactory _factory;

    public HealthEndpointApiTests(BookVerseWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReturnBareStatusOnly(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
