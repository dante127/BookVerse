using BookVerse.Api.Observability;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BookVerse.ApiTests;

/// <summary>
/// PROD-2: the OpenTelemetry registration lives in MetricsSetup so it can be verified without
/// booting a second host. It must always register a metrics reader pipeline (so the BookVerse
/// instruments leave the process) and only add an OTLP exporter when a collector is configured.
/// </summary>
public class MetricsSetupTests
{
    [Fact]
    public void AddBookVerseMetrics_RegistersOpenTelemetryHostingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBookVerseMetrics(new ConfigurationBuilder().Build(), otlpEndpointConfigured: false);

        var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>()
            .Where(s => s.GetType().FullName!.StartsWith("OpenTelemetry"))
            .Should().NotBeEmpty("a metrics reader/exporter pipeline must be registered for the meters to be observed");
    }

    [Fact]
    public void IsOtlpEndpointConfigured_DetectsConfiguredEndpoint()
    {
        var withEndpoint = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Otlp:Endpoint"] = "http://collector:4317" })
            .Build();

        MetricsSetup.IsOtlpEndpointConfigured(withEndpoint).Should().BeTrue();
    }
}
