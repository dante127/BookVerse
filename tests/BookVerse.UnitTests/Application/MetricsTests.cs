using System.Diagnostics;
using System.Diagnostics.Metrics;
using BookVerse.Infrastructure.Caching;
using BookVerse.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookVerse.UnitTests.Application;

/// <summary>
/// Phase 7 (OPS): the Meter API is the contract ops teams scrape, so instrumented
/// counters are asserted with a MeterListener rather than by reading private state.
/// </summary>
public class MetricsTests : IDisposable
{
    private readonly Dictionary<string, long> _totals = new();
    private readonly MeterListener _listener;

    public MetricsTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BookVerseMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            _totals[instrument.Name] = _totals.GetValueOrDefault(instrument.Name) + value;
        });
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task CacheGet_WithoutRedis_IncrementsUnavailable()
    {
        var service = new RedisCacheService(NullLogger<RedisCacheService>.Instance, redis: null);

        var value = await service.GetAsync<string>("any-key");

        value.Should().BeNull();
        _totals.GetValueOrDefault("bookverse.cache.unavailable").Should().Be(1);
    }

    [Fact]
    public void WorkerIteration_Failure_IncrementsIterationsErrorsAndDuration()
    {
        BookVerseMetrics.RecordWorkerIteration("TestWorker", 12.5, success: false);

        _totals.GetValueOrDefault("bookverse.worker.iterations").Should().Be(1);
        _totals.GetValueOrDefault("bookverse.worker.errors").Should().Be(1);
    }

    [Fact]
    public void WorkerIteration_Success_IncrementsIterationsOnly()
    {
        BookVerseMetrics.RecordWorkerIteration("TestWorker", 3.0, success: true);

        _totals.GetValueOrDefault("bookverse.worker.iterations").Should().Be(1);
        _totals.Should().NotContainKey("bookverse.worker.errors");
    }

    [Fact]
    public void ElapsedMs_ReturnsPositiveDuration()
    {
        var startedAt = Stopwatch.GetTimestamp();
        Thread.Sleep(15);

        BookVerseMetrics.ElapsedMs(startedAt).Should().BeGreaterThan(0);
    }
}
