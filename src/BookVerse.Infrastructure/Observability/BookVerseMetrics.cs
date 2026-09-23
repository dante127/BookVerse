using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BookVerse.Infrastructure.Observability;

/// <summary>
/// Phase 7 (OPS): structured metrics for the two things the app previously could not
/// observe — cache efficiency (hit ratio) and background worker health.
/// Exposed via the .NET Meter API (OpenTelemetry/OTLP collectors pick them up as-is).
/// </summary>
public static class BookVerseMetrics
{
    public const string MeterName = "BookVerse";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "bookverse.cache.hits", description: "Cache reads that returned a stored value.");

    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "bookverse.cache.misses", description: "Cache reads that found the key absent.");

    public static readonly Counter<long> CacheUnavailable = Meter.CreateCounter<long>(
        "bookverse.cache.unavailable", description: "Operations served on the graceful fallback path (Redis down/absent).");

    public static readonly Counter<long> CacheFailures = Meter.CreateCounter<long>(
        "bookverse.cache.errors", description: "Redis operation exceptions swallowed by the fallback design.");

    public static readonly Counter<long> WorkerIterations = Meter.CreateCounter<long>(
        "bookverse.worker.iterations", description: "Completed iterations per background worker.");

    public static readonly Counter<long> WorkerFailures = Meter.CreateCounter<long>(
        "bookverse.worker.errors", description: "Failed iterations per background worker (exception caught and logged).");

    public static readonly Histogram<double> WorkerIterationDuration = Meter.CreateHistogram<double>(
        "bookverse.worker.iteration.duration", unit: "ms", description: "Wall-clock duration of a worker iteration.");

    public static double ElapsedMs(long startedTimestamp) => Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    public static void RecordWorkerIteration(string workerName, double elapsedMs, bool success)
    {
        var tag = new KeyValuePair<string, object?>("worker", workerName);
        WorkerIterations.Add(1, tag);
        WorkerIterationDuration.Record(elapsedMs, tag);
        if (!success)
        {
            WorkerFailures.Add(1, tag);
        }
    }
}
