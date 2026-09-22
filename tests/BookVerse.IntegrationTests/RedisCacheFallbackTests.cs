using BookVerse.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookVerse.IntegrationTests;

public class RedisCacheFallbackTests
{
    [Fact]
    public async Task GetAsync_WhenRedisNull_ShouldReturnDefaultWithoutThrowing()
    {
        // Arrange
        var cacheService = new RedisCacheService(NullLogger<RedisCacheService>.Instance, null);

        // Act
        var result = await cacheService.GetAsync<string>("any_key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenRedisNull_ShouldCompleteGracefullyWithoutThrowing()
    {
        // Arrange
        var cacheService = new RedisCacheService(NullLogger<RedisCacheService>.Instance, null);

        // Act & Assert
        var act = async () => await cacheService.SetAsync("any_key", "any_value", TimeSpan.FromMinutes(5));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_WhenRedisNull_ShouldCompleteGracefullyWithoutThrowing()
    {
        // Arrange
        var cacheService = new RedisCacheService(NullLogger<RedisCacheService>.Instance, null);

        // Act & Assert
        var act = async () => await cacheService.RemoveAsync("any_key");
        await act.Should().NotThrowAsync();
    }
}
