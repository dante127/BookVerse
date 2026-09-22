using BookVerse.Application.Common.Interfaces;
using BookVerse.Infrastructure.BackgroundJobs;
using BookVerse.Infrastructure.Caching;
using BookVerse.Infrastructure.Identity;
using BookVerse.Infrastructure.Persistence;
using BookVerse.Infrastructure.Persistence.Interceptors;
using BookVerse.Infrastructure.Persistence.Seeding;
using BookVerse.Infrastructure.Recommendations;
using BookVerse.Infrastructure.Search;
using BookVerse.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BookVerse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Interceptors
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // Database Configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\mssqllocaldb;Database=BookVerseDb;Trusted_Connection=True;MultipleActiveResultSets=true";

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var auditableInterceptor = sp.GetRequiredService<AuditableEntityInterceptor>();
            var eventsInterceptor = sp.GetRequiredService<DispatchDomainEventsInterceptor>();

            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
            });

            options.AddInterceptors(auditableInterceptor, eventsInterceptor);
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Redis Caching with resilient connection
        var redisConnection = configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            try
            {
                var multiplexer = ConnectionMultiplexer.Connect(redisConnection);
                services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            }
            catch (Exception ex)
            {
                var logger = services.BuildServiceProvider().GetService<ILogger<RedisCacheService>>();
                logger?.LogWarning(ex, "Could not establish initial connection to Redis. Starting in fallback cache mode.");
            }
        }

        services.AddSingleton<ICacheService, RedisCacheService>();

        // Core Infrastructure Services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IReviewModerationService, ReviewModerationService>();
        services.AddScoped<ISearchService, SqlSearchService>();
        services.AddScoped<IRecommendationService, DeterministicRecommendationService>();
        services.AddScoped<DatabaseSeeder>();

        // Background Workers
        services.AddHostedService<TrendingRecalculationWorker>();
        services.AddHostedService<TokenCleanupWorker>();
        services.AddHostedService<AggregateReconciliationWorker>();

        return services;
    }
}
