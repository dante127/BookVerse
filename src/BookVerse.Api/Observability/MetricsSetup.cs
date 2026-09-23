using BookVerse.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;

namespace BookVerse.Api.Observability;

/// <summary>
/// PROD-2: OpenTelemetry metrics wiring. Without a reader/exporter the BookVerse instruments
/// (cache hit ratio, worker health) never leave the process. Collection is always registered;
/// OTLP export is added only when a collector endpoint is configured, so a dev box with no
/// collector pays no background export while the OTLP pipeline stays one env-var away.
/// </summary>
public static class MetricsSetup
{
    public static IServiceCollection AddBookVerseMetrics(
        this IServiceCollection services,
        IConfiguration configuration,
        bool otlpEndpointConfigured = false)
    {
        services.AddOpenTelemetry().WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("bookverse-api"))
                .AddMeter(BookVerseMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (otlpEndpointConfigured)
            {
                metrics.AddOtlpExporter();
            }
        });

        return services;
    }

    public static bool IsOtlpEndpointConfigured(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration["Otlp:Endpoint"])
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
}
